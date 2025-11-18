// Pos.Persistence/Services/OpeningStockService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Domain.Entities;                // StockDoc, StockEntry, Item
using Pos.Domain.Services;                // IOpeningStockService
using Pos.Domain.Utils;                   // GuidUtility
using Pos.Domain.Models.OpeningStock;     // DTOs (moved)
using Pos.Persistence.Sync;               // IOutboxWriter

namespace Pos.Persistence.Services
{
    public sealed class OpeningStockService : IOpeningStockService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ILogger<OpeningStockService> _log;
        private readonly IOutboxWriter _outbox;
        private readonly IGlPostingService _gl;

        public OpeningStockService(
            IDbContextFactory<PosClientDbContext> dbf,
            ILogger<OpeningStockService> log,
            IOutboxWriter outbox,
            IGlPostingService gl)
        {
            _dbf = dbf;
            _log = log;
            _outbox = outbox;
            _gl = gl;
        }

        private static string ComposeDisplayName(Item i)
        {
            var baseName = string.IsNullOrWhiteSpace(i.Product?.Name) ? (i.Name ?? "") : i.Product!.Name!;
            var parts = new List<string> { baseName };
            if (!string.IsNullOrWhiteSpace(i.Variant1Value)) parts.Add(i.Variant1Value!);
            if (!string.IsNullOrWhiteSpace(i.Variant2Value)) parts.Add(i.Variant2Value!);
            return string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        public async Task<List<OpeningDocSummaryDto>> GetOpeningDocSummariesAsync(
            InventoryLocationType locationType,
            int locationId,
            StockDocStatus? statusFilter = null,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var docsQ = db.StockDocs.AsNoTracking()
                .Where(d => d.DocType == StockDocType.Opening
                         && d.LocationType == locationType
                         && d.LocationId == locationId);
            if (statusFilter.HasValue)
                docsQ = docsQ.Where(d => d.Status == statusFilter.Value);
            var docs = await docsQ
                .OrderByDescending(d => d.Id)
                .Select(d => new { d.Id, d.Status, d.EffectiveDateUtc, d.Note })
                .ToListAsync(ct);
            if (docs.Count == 0) return new();
            var draftIds = docs.Where(d => d.Status == StockDocStatus.Draft).Select(d => d.Id).ToList();
            var nonDraftIds = docs.Where(d => d.Status != StockDocStatus.Draft).Select(d => d.Id).ToList();
            var draftAgg = draftIds.Count == 0
                ? new Dictionary<int, (int count, decimal qty, decimal value)>()
                : await db.OpeningStockDraftLines.AsNoTracking()
                    .Where(l => draftIds.Contains(l.StockDocId))
                    .GroupBy(l => l.StockDocId)
                    .Select(g => new
                    {
                        DocId = g.Key,
                        Count = g.Count(),
                        Qty = g.Sum(x => x.Qty),
                        Value = g.Sum(x => x.Qty * x.UnitCost)
                    })
                    .ToDictionaryAsync(x => x.DocId, x => (x.Count, x.Qty, x.Value), ct);
            var postedAgg = nonDraftIds.Count == 0
                ? new Dictionary<int, (int count, decimal qty, decimal value)>()
                : await db.StockEntries.AsNoTracking()
                    .Where(se => se.StockDocId.HasValue && nonDraftIds.Contains(se.StockDocId.Value))
                    .Where(se => se.RefType == "Opening" && se.QtyChange > 0)
                    .GroupBy(se => se.StockDocId!.Value)
                    .Select(g => new
                    {
                        DocId = g.Key,
                        Count = g.Count(),
                        Qty = g.Sum(x => x.QtyChange),
                        Value = g.Sum(x => x.QtyChange * x.UnitCost)
                    })
                    .ToDictionaryAsync(x => x.DocId, x => (x.Count, x.Qty, x.Value), ct);
            var result = new List<OpeningDocSummaryDto>(docs.Count);
            foreach (var d in docs)
            {
                var agg = d.Status == StockDocStatus.Draft
                    ? (draftAgg.TryGetValue(d.Id, out var a) ? a : (0, 0m, 0m))
                    : (postedAgg.TryGetValue(d.Id, out var b) ? b : (0, 0m, 0m));
                // Deconstruct tuple
                var (lineCount, totalQty, totalValue) = agg;
                result.Add(new OpeningDocSummaryDto
                {
                    Id = d.Id,
                    EffectiveDateUtc = d.EffectiveDateUtc,
                    LineCount = lineCount,
                    TotalQty = totalQty,
                    TotalValue = totalValue,
                    Note = d.Note,
                    Status = d.Status
                });
            }
            return result;
        }

        public async Task<(StockDoc? Doc, List<(int ItemId, string Sku, string Display, decimal Qty, decimal UnitCost, string? Note)> Lines)>
            GetLatestDraftForLocationAsync(InventoryLocationType locationType, int locationId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var draft = await db.StockDocs.AsNoTracking()
                .Where(d => d.DocType == StockDocType.Opening
                         && d.Status == StockDocStatus.Draft
                         && d.LocationType == locationType
                         && d.LocationId == locationId)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync(ct);
            if (draft is null) return (null, new());
            var dlines = await db.OpeningStockDraftLines.AsNoTracking()
                .Where(x => x.StockDocId == draft.Id)
                .Select(x => new { x.ItemId, x.Qty, x.UnitCost, x.Note })
                .ToListAsync(ct);
            var ids = dlines.Select(x => x.ItemId).Distinct().ToList();
            var items = await db.Items.AsNoTracking()
                .Include(i => i.Product)
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, Display = ComposeDisplayName(i) })
                .ToListAsync(ct);
            var byId = items.ToDictionary(x => x.Id, x => x);
            var lines = dlines.Select(l =>
            {
                var meta = byId[l.ItemId];
                return (l.ItemId, meta.Sku, meta.Display, l.Qty, l.UnitCost, l.Note);
            }).ToList();
            return (draft, lines);
        }

        public async Task<(StockDoc Doc, List<(int ItemId, string Sku, string Display, decimal Qty, decimal UnitCost, string? Note)> Lines)>
            ReadDocumentForUiAsync(int stockDocId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var doc = await db.StockDocs.AsNoTracking().FirstAsync(d => d.Id == stockDocId, ct);
            List<(int ItemId, decimal Qty, decimal UnitCost, string? Note)> core;
            if (doc.Status == StockDocStatus.Draft)
            {
                var dlines = await db.OpeningStockDraftLines.AsNoTracking()
                    .Where(x => x.StockDocId == doc.Id)
                    .Select(x => new { x.ItemId, x.Qty, x.UnitCost, x.Note })
                    .ToListAsync(ct);
                core = dlines.Select(x => (x.ItemId, x.Qty, x.UnitCost, x.Note)).ToList();
            }
            else
            {
                var lines = await db.StockEntries.AsNoTracking()
                    .Where(se => se.StockDocId == doc.Id && (se.RefType == "Opening" || se.RefType == "OpeningVoid"))
                    .Select(se => new { se.ItemId, se.QtyChange, se.UnitCost, se.Note })
                    .ToListAsync(ct);
                var postedIn = lines.Where(l => l.QtyChange > 0).ToList();
                core = postedIn.Select(l => (l.ItemId, l.QtyChange, l.UnitCost, l.Note)).ToList();
            }
            var ids = core.Select(c => c.ItemId).Distinct().ToList();
            var items = await db.Items.AsNoTracking()
                .Include(i => i.Product)
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, Display = ComposeDisplayName(i) })
                .ToListAsync(ct);
            var byId = items.ToDictionary(x => x.Id, x => x);
            var ui = core.Select(c => {
                var m = byId[c.ItemId];
                return (c.ItemId, m.Sku, m.Display, c.Qty, c.UnitCost, c.Note);
            }).ToList();
            return (doc, ui);
        }

        public async Task<(string Sku, string Display)> GetItemDisplayByIdAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var i = await db.Items.AsNoTracking()
                .Include(x => x.Product)
                .FirstAsync(x => x.Id == itemId, ct);
            return (i.Sku, ComposeDisplayName(i));
        }

        public async Task<Dictionary<string, string>> GetDisplayBySkuAsync(IEnumerable<string> skus, CancellationToken ct = default)
        {
            var list = skus.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return new(StringComparer.OrdinalIgnoreCase);
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var items = await db.Items.AsNoTracking()
                .Include(i => i.Product)
                .Where(i => list.Contains(i.Sku))
                .Select(i => new { i.Sku, Display = ComposeDisplayName(i) })
                .ToListAsync(ct);
            return items.ToDictionary(x => x.Sku, x => x.Display, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<List<(string Sku, string Display)>> GetAllActiveItemDisplaysAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Items.AsNoTracking()
                .Where(i => i.IsActive && !i.IsVoided)
                .Include(i => i.Product)
                .OrderBy(i => i.Sku)
                .Select(i => new ValueTuple<string, string>(i.Sku, ComposeDisplayName(i)))
                .ToListAsync(ct);
        }

        public async Task<List<(string Sku, string Display)>> GetMissingOpeningItemDisplaysAsync(
            InventoryLocationType locationType, int locationId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var rows = await db.Items.AsNoTracking()
                .Where(i => i.IsActive && !i.IsVoided)
                .Where(i => !db.StockEntries.Any(se =>
                    se.ItemId == i.Id &&
                    se.RefType == "Opening" &&
                    se.LocationType == locationType &&
                    se.LocationId == locationId))
                .Include(i => i.Product)
                .OrderBy(i => i.Sku)
                .Select(i => new ValueTuple<string, string>(i.Sku, ComposeDisplayName(i)))
                .ToListAsync(ct);
            return rows;
        }

        public async Task UpdateEffectiveDateAsync(int stockDocId, DateTime effectiveDateLocal, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var doc = await db.StockDocs.FirstAsync(d => d.Id == stockDocId, ct);
            if (doc.Status != StockDocStatus.Draft)
                throw new InvalidOperationException("Document is locked.");
            var newUtc = effectiveDateLocal.ToUniversalTime();
            if (doc.EffectiveDateUtc != newUtc)
            {
                doc.EffectiveDateUtc = newUtc;
                await db.SaveChangesAsync(ct);
            }
        }

        public async Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            switch (req.LocationType)
            {
                case InventoryLocationType.Outlet:
                    if (!await db.Outlets.AnyAsync(o => o.Id == req.LocationId, ct))
                        throw new InvalidOperationException("Outlet not found.");
                    break;
                case InventoryLocationType.Warehouse:
                    if (!await db.Warehouses.AnyAsync(w => w.Id == req.LocationId, ct))
                        throw new InvalidOperationException("Warehouse not found.");
                    break;
                default:
                    throw new InvalidOperationException("Invalid location type.");
            }
            var doc = new StockDoc
            {
                DocType = StockDocType.Opening,
                Status = StockDocStatus.Draft,
                LocationType = req.LocationType,
                LocationId = req.LocationId,
                EffectiveDateUtc = req.EffectiveDateUtc,
                Note = req.Note,
                CreatedByUserId = req.CreatedByUserId
            };
            db.StockDocs.Add(doc);
            await db.SaveChangesAsync(ct);
            return doc;
        }

        public async Task DeleteDraftAsync(int stockDocId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var doc = await db.StockDocs.FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) return; // idempotent
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status != StockDocStatus.Draft) throw new InvalidOperationException("Only Draft opening stock can be deleted.");
            var drafts = await db.OpeningStockDraftLines.Where(x => x.StockDocId == doc.Id).ToListAsync(ct);
            db.OpeningStockDraftLines.RemoveRange(drafts);
            await db.SaveChangesAsync(ct);
            foreach (var line in drafts)
            {
                var topicLine = nameof(OpeningStockDraftLine);
                var publicIdLine = GuidUtility.FromString($"{topicLine}:{line.Id}");
                await _outbox.EnqueueDeleteAsync(db, topicLine, publicIdLine, ct);
            }
            await db.SaveChangesAsync(ct);
            db.StockDocs.Remove(doc);
            await db.SaveChangesAsync(ct);
            {
                var topicDoc = nameof(StockDoc);
                var publicIdDoc = GuidUtility.FromString($"{topicDoc}:{doc.Id}");
                await _outbox.EnqueueDeleteAsync(db, topicDoc, publicIdDoc, ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task<OpeningStockValidationResult> ValidateLinesAsync(
            int stockDocId, IEnumerable<OpeningStockLineDto> lines, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var res = new OpeningStockValidationResult();
            var doc = await db.StockDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) { res.Errors.Add(new() { Field = "StockDocId", Message = "Opening document not found." }); return res; }
            if (doc.DocType != StockDocType.Opening) { res.Errors.Add(new() { Field = "DocType", Message = "Invalid document type." }); return res; }
            if (doc.Status == StockDocStatus.Locked) { res.Errors.Add(new() { Field = "Status", Message = "Document is locked." }); return res; }
            if (doc.Status == StockDocStatus.Voided) { res.Errors.Add(new() { Field = "Status", Message = "Document is voided." }); return res; }
            var skus = lines.Select(l => (l.Sku ?? "").Trim()).Where(s => s.Length > 0).Distinct().ToList();
            var items = await db.Items.Where(i => skus.Contains(i.Sku)).Select(i => new { i.Id, i.Sku }).ToListAsync(ct);
            var itemBySku = items.ToDictionary(x => x.Sku, x => x.Id, StringComparer.OrdinalIgnoreCase);
            int row = 0;
            foreach (var l in lines)
            {
                var sku = (l.Sku ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sku))
                    res.Errors.Add(new() { RowIndex = row, Field = "Sku", Message = "SKU is required." });
                if (!string.IsNullOrEmpty(sku) && !itemBySku.ContainsKey(sku))
                    res.Errors.Add(new() { RowIndex = row, Field = "Sku", Sku = sku, Message = "SKU not found. Create item first." });
                if (l.Qty <= 0)
                    res.Errors.Add(new() { RowIndex = row, Field = "Qty", Sku = sku, Message = "Quantity must be > 0." });
                if (l.UnitCost < 0)
                    res.Errors.Add(new() { RowIndex = row, Field = "UnitCost", Sku = sku, Message = "Unit cost cannot be negative." });
                row++;
            }
            return res;
        }

        public async Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var doc = await db.StockDocs.FirstOrDefaultAsync(d => d.Id == req.StockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) throw new InvalidOperationException("Document is locked and cannot be edited.");
            if (doc.Status == StockDocStatus.Voided) throw new InvalidOperationException("Document is voided and cannot be edited.");
            var val = await ValidateLinesAsync(req.StockDocId, req.Lines, ct);
            if (!val.Ok)
            {
                var first = val.Errors.First();
                throw new InvalidOperationException($"Validation failed at row {(first.RowIndex ?? -1)} ({first.Field}): {first.Message}");
            }
            var skus = req.Lines.Select(l => l.Sku.Trim()).Distinct().ToList();
            var items = await db.Items.Where(i => skus.Contains(i.Sku)).Select(i => new { i.Id, i.Sku }).ToListAsync(ct);
            var itemBySku = items.ToDictionary(x => x.Sku, x => x.Id, StringComparer.OrdinalIgnoreCase);
            var existingDraft = db.OpeningStockDraftLines.Where(x => x.StockDocId == doc.Id);
            if (req.ReplaceAll)
            {
                db.OpeningStockDraftLines.RemoveRange(existingDraft);
            }
            else
            {
                var cache = await existingDraft.ToListAsync(ct);
                foreach (var line in req.Lines)
                {
                    var itemId = itemBySku[line.Sku.Trim()];
                    var dup = cache.FirstOrDefault(x => x.ItemId == itemId);
                    if (dup != null) db.OpeningStockDraftLines.Remove(dup);
                }
            }

            foreach (var l in req.Lines)
            {
                var itemId = itemBySku[l.Sku.Trim()];
                db.OpeningStockDraftLines.Add(new OpeningStockDraftLine
                {
                    StockDocId = doc.Id,
                    ItemId = itemId,
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Note = l.Note
                });
            }
            await db.SaveChangesAsync(ct);
        }

        public async Task PostAsync(int stockDocId, int postedByUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var doc = await db.StockDocs.FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) throw new InvalidOperationException("Locked document cannot be posted.");
            if (doc.Status == StockDocStatus.Voided) throw new InvalidOperationException("Voided document cannot be posted.");
            var draftLines = await db.OpeningStockDraftLines
                .Where(x => x.StockDocId == doc.Id)
                .ToListAsync(ct);
            if (draftLines.Count == 0)
                throw new InvalidOperationException("No lines to post.");
            // Existing opening entries for this document
            var existingEntries = await db.StockEntries
                .Where(e => e.RefType == "Opening" && e.RefId == doc.Id)
                .ToListAsync(ct);
            // Aggregate existing qty per item
            var existingByItem = existingEntries
                .GroupBy(e => e.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Qty = g.Sum(x => x.QtyChange),
                        // last unit cost snapshot – used only for reference
                        UnitCost = g.OrderByDescending(x => x.Id).First().UnitCost
                    });
            // Aggregate desired qty per item from draft lines
            var targetByItem = draftLines
                .GroupBy(l => l.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Qty = g.Sum(x => x.Qty),
                        // we’ll just use the last unit cost from draft lines
                        UnitCost = g.OrderByDescending(x => x.Id).First().UnitCost
                    });
            // Union of all item ids
            var allItemIds = existingByItem.Keys.Union(targetByItem.Keys).Distinct();
            foreach (var itemId in allItemIds)
            {
                var oldQty = existingByItem.TryGetValue(itemId, out var old)
                    ? old.Qty
                    : 0m;
                var newInfo = targetByItem.TryGetValue(itemId, out var @new)
                    ? @new
                    : new { Qty = 0m, UnitCost = 0m };
                var deltaQty = newInfo.Qty - oldQty;
                if (deltaQty == 0m) continue; // no change for this item
                db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = itemId,
                    QtyChange = deltaQty,
                    UnitCost = newInfo.UnitCost,
                    LocationType = doc.LocationType,
                    LocationId = doc.LocationId,
                    RefType = "Opening",
                    RefId = doc.Id,
                    Ts = doc.EffectiveDateUtc,
                    Note = "Opening stock adjustment"
                });
            }
            doc.Status = StockDocStatus.Posted;
            doc.PostedByUserId = postedByUserId;
            doc.PostedAtUtc = DateTime.UtcNow;
            db.OpeningStockDraftLines.RemoveRange(draftLines);
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, doc, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var doc = await db.StockDocs
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) return;
            var hasPostedEntries = (doc.Lines?.Any() == true) || doc.Status == StockDocStatus.Posted;
            if (!hasPostedEntries)
                throw new InvalidOperationException("Document must be posted before locking.");
            // Load all opening stock entries for this doc
            var entries = await db.StockEntries
                .Where(e => e.RefType == "Opening" && e.RefId == doc.Id)
                .ToListAsync(ct);
            // TODO: resolve the "Opening Stock Offset" account id from settings/coA
            var offsetAccountId = await ResolveOpeningOffsetAccountIdAsync(db, doc, ct);
            // Let GL posting service do delta-based posting (similar to purchases)
            await _gl.PostOpeningStockAsync(doc, entries, offsetAccountId, ct);
            doc.Status = StockDocStatus.Locked;
            doc.LockedByUserId = adminUserId;
            doc.LockedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, doc, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var doc = await db.StockDocs
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status != StockDocStatus.Locked) return;
            // <<< NEW: inactivate GL rows for this Opening chain >>>
            await _gl.UnlockOpeningStockAsync(doc, ct);
            doc.Status = StockDocStatus.Posted;
            doc.LockedByUserId = null;
            doc.LockedAtUtc = null;
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, doc, ct);
            await db.SaveChangesAsync(ct);
        }


        public async Task VoidAsync(int stockDocId, int userId, string? reason, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var doc = await db.StockDocs
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked)
                throw new InvalidOperationException("Locked documents cannot be voided. Unlock first.");
            if (doc.Status == StockDocStatus.Voided)
                return;
            if (doc.Status == StockDocStatus.Draft)
            {
                var drafts = db.OpeningStockDraftLines.Where(x => x.StockDocId == doc.Id);
                db.OpeningStockDraftLines.RemoveRange(drafts);
                doc.Status = StockDocStatus.Voided;
                doc.VoidedByUserId = userId;
                doc.VoidedAtUtc = DateTime.UtcNow;
                doc.VoidReason = reason;
                await db.SaveChangesAsync(ct);
                await _outbox.EnqueueUpsertAsync(db, doc, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }

            if (doc.Status == StockDocStatus.Posted)
            {
                var postedEntries = await db.StockEntries
                    .Where(se => se.StockDocId == doc.Id && se.RefType == "Opening")
                    .ToListAsync(ct);
                foreach (var se in postedEntries)
                {
                    db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = se.ItemId,
                        QtyChange = -se.QtyChange,
                        UnitCost = se.UnitCost,
                        LocationType = se.LocationType,
                        LocationId = se.LocationId,
                        RefType = "OpeningVoid",
                        RefId = doc.Id,
                        Ts = DateTime.UtcNow,
                        Note = string.IsNullOrWhiteSpace(se.Note) ? "Void reversal" : (se.Note + " (Void)")
                    });
                }
                doc.Status = StockDocStatus.Voided;
                doc.VoidedByUserId = userId;
                doc.VoidedAtUtc = DateTime.UtcNow;
                doc.VoidReason = reason;
                await db.SaveChangesAsync(ct);
                await _outbox.EnqueueUpsertAsync(db, doc, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            throw new InvalidOperationException("Unsupported state for void.");
        }

        public Task<StockDoc?> GetAsync(int stockDocId, CancellationToken ct = default)
        {
            return _dbf.CreateDbContextAsync(ct)
                .ContinueWith(async t =>
                {
                    await using var db = t.Result;
                    return await db.StockDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
                }, ct).Unwrap();
        }

        private static async Task<int> ResolveOpeningOffsetAccountIdAsync(
    PosClientDbContext db,
    StockDoc doc,
    CancellationToken ct)
        {
            // Single header for all openings (company-level header)
            // Seeded in CoA as: new("1141", "Stock openings", AccountType.Asset, "114", isHeader:true, allowPosting:false, NormalSide.Debit)
            const string headerCode = "1141";
            var header = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == headerCode && a.IsHeader, ct);
            if (header == null)
                throw new InvalidOperationException("Chart of Accounts missing header '1141 - Stock openings'.");
            var isWarehouse = doc.LocationType == InventoryLocationType.Warehouse;
            var suffix = doc.LocationId.ToString("D2");
            var childCode = isWarehouse ? $"1141-W{suffix}" : $"1141-O{suffix}";
            // If exists, return it
            var existing = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == childCode, ct);
            if (existing != null)
                return existing.Id;
            // Resolve a friendly name
            string locName;
            if (isWarehouse)
            {
                var wh = await db.Warehouses
                    .AsNoTracking()
                    .Where(w => w.Id == doc.LocationId)
                    .Select(w => new { w.Name })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("Warehouse not found for Opening Stock document.");
                locName = wh.Name;
            }
            else
            {
                var outlet = await db.Outlets
                    .AsNoTracking()
                    .Where(o => o.Id == doc.LocationId)
                    .Select(o => new { o.Name })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("Outlet not found for Opening Stock document.");
                locName = outlet.Name;
            }
            // Create a posting child under 1141
            var acc = new Account
            {
                Code = childCode,
                Name = $"Opening stock · {locName}",
                Type = header.Type,                // Asset
                NormalSide = header.NormalSide,    // Debit
                IsHeader = false,
                AllowPosting = true,
                ParentId = header.Id,              // <-- attach under 1141
                // Scope outlet children; warehouses remain company-scoped
                OutletId = isWarehouse ? (int?)null : doc.LocationId,
                OpeningDebit = 0m,
                OpeningCredit = 0m,
                IsOpeningLocked = false,
                IsActive = true,
                IsSystem = false
            };
            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);
            return acc.Id;
        }
    }
}
