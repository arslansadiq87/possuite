// Pos.Persistence/Services/OpeningStockService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Domain;                     // InventoryLocationType, etc.
using Pos.Domain.Entities;            // StockDoc, StockEntry, OpeningStockDraftLine ...
using Pos.Persistence.Features.OpeningStock;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Opening Stock flow (Outlet & Warehouse):
    /// Draft (OpeningStockDraftLines) -> Post (writes StockEntries IN) -> Lock.
    /// Void:
    ///  - Draft: mark void (no stock impact)
    ///  - Posted (unlocked): write reversal StockEntries and mark void
    /// </summary>
    public interface IOpeningStockService
    {
        Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default);
        Task<OpeningStockValidationResult> ValidateLinesAsync(int stockDocId, IEnumerable<OpeningStockLineDto> lines, CancellationToken ct = default);
        Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default);

        Task PostAsync(int stockDocId, int postedByUserId, CancellationToken ct = default);
        Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
        Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);

        Task VoidAsync(int stockDocId, int userId, string? reason, CancellationToken ct = default);

        Task<StockDoc?> GetAsync(int stockDocId, CancellationToken ct = default);
    }

    public sealed class OpeningStockService : IOpeningStockService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ILogger<OpeningStockService> _log;

        public OpeningStockService(IDbContextFactory<PosClientDbContext> dbf, ILogger<OpeningStockService> log)
        {
            _dbf = dbf;
            _log = log;
        }

        public async Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Validate location
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

        public async Task<OpeningStockValidationResult> ValidateLinesAsync(int stockDocId, IEnumerable<OpeningStockLineDto> lines, CancellationToken ct = default)
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

        /// <summary>
        /// Save/replace Draft lines WITHOUT touching StockEntries (no on-hand impact).
        /// </summary>
        public async Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var doc = await db.StockDocs.FirstOrDefaultAsync(d => d.Id == req.StockDocId, ct);
            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) throw new InvalidOperationException("Document is locked and cannot be edited.");
            if (doc.Status == StockDocStatus.Voided) throw new InvalidOperationException("Document is voided and cannot be edited.");

            // Validate input
            var val = await ValidateLinesAsync(req.StockDocId, req.Lines, ct);
            if (!val.Ok)
            {
                var first = val.Errors.First();
                throw new InvalidOperationException($"Validation failed at row {(first.RowIndex ?? -1)} ({first.Field}): {first.Message}");
            }

            // Map SKUs to ItemIds
            var skus = req.Lines.Select(l => l.Sku.Trim()).Distinct().ToList();
            var items = await db.Items.Where(i => skus.Contains(i.Sku)).Select(i => new { i.Id, i.Sku }).ToListAsync(ct);
            var itemBySku = items.ToDictionary(x => x.Sku, x => x.Id, StringComparer.OrdinalIgnoreCase);

            // Replace vs merge in the DRAFT LINES table
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

            // insert new draft lines
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

        /// <summary>
        /// Post: convert Draft -> Posted by materializing StockEntries (IN) from DraftLines.
        /// </summary>
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

            // Write IN entries
            foreach (var l in draftLines)
            {
                db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = l.ItemId,
                    QtyChange = l.Qty,                // IN
                    UnitCost = l.UnitCost,
                    LocationType = doc.LocationType,
                    LocationId = doc.LocationId,
                    RefType = "Opening",
                    RefId = doc.Id,
                    Ts = doc.EffectiveDateUtc,
                    Note = l.Note ?? ""
                });
            }

            // Transition to Posted
            doc.Status = StockDocStatus.Posted; // ensure enum has Posted
            doc.PostedByUserId = postedByUserId;
            doc.PostedAtUtc = DateTime.UtcNow;

            // Remove draft lines now that they're materialized
            db.OpeningStockDraftLines.RemoveRange(draftLines);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        /// <summary>
        /// Lock is allowed only after Post; no stock writes here, just freeze.
        /// </summary>
        public async Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var doc = await db.StockDocs
                .Include(d => d.Lines) // Lines = StockEntries for this doc
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);

            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) return; // idempotent

            var hasPostedEntries = (doc.Lines?.Any() == true) || doc.Status == StockDocStatus.Posted;
            if (!hasPostedEntries)
                throw new InvalidOperationException("Document must be posted before locking.");

            doc.Status = StockDocStatus.Locked;
            doc.LockedByUserId = adminUserId;
            doc.LockedAtUtc = DateTime.UtcNow;

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
            if (doc.Status != StockDocStatus.Locked) return; // idempotent

            // Unlock → back to Posted (not Draft)
            doc.Status = StockDocStatus.Posted;
            doc.LockedByUserId = null;
            doc.LockedAtUtc = null;

            await db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Void:
        ///  - Draft: mark void, delete draft lines (no stock impact)
        ///  - Posted (unlocked): add reversal StockEntries (OUT) and mark void
        ///  - Locked: forbid (Unlock first if policy allows)
        /// </summary>
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
                return; // idempotent

            if (doc.Status == StockDocStatus.Draft)
            {
                // Remove any draft lines and mark void
                var drafts = db.OpeningStockDraftLines.Where(x => x.StockDocId == doc.Id);
                db.OpeningStockDraftLines.RemoveRange(drafts);

                doc.Status = StockDocStatus.Voided;
                doc.VoidedByUserId = userId;
                doc.VoidedAtUtc = DateTime.UtcNow;
                doc.VoidReason = reason;

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }

            // Posted (unlocked) → reversal OUT
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
                        QtyChange = -se.QtyChange, // OUT
                        UnitCost = se.UnitCost,
                        LocationType = se.LocationType,
                        LocationId = se.LocationId,
                        RefType = "OpeningVoid",
                        RefId = doc.Id,
                        Ts = DateTime.UtcNow, // or se.Ts if you want the same effective date
                        Note = string.IsNullOrWhiteSpace(se.Note) ? "Void reversal" : (se.Note + " (Void)")
                    });
                }

                doc.Status = StockDocStatus.Voided;
                doc.VoidedByUserId = userId;
                doc.VoidedAtUtc = DateTime.UtcNow;
                doc.VoidReason = reason;

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
                    // Lines = StockEntries; keeps parity with your previous pattern
                    return await db.StockDocs.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == stockDocId, ct);
                }, ct).Unwrap();
        }
    }
}
