using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Features.OpeningStock;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Orchestrates Opening Stock: create draft header, upsert lines, lock with guards.
    /// Uses StockEntry as canonical movement with RefType="Opening" and RefId=StockDoc.Id.
    /// </summary>
    public interface IOpeningStockService
    {
        Task<StockDoc> CreateDraftAsync(OpeningStockCreateRequest req, CancellationToken ct = default);
        Task<OpeningStockValidationResult> ValidateLinesAsync(int stockDocId, IEnumerable<OpeningStockLineDto> lines, CancellationToken ct = default);
        Task UpsertLinesAsync(OpeningStockUpsertRequest req, CancellationToken ct = default);
        Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
        Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default);
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
            // Permission: should be enforced in caller (Admin only). This service assumes already-checked.
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Validate location exists
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
            if (doc.Status != StockDocStatus.Draft) { res.Errors.Add(new() { Field = "Status", Message = "Document is locked." }); return res; }
            if (doc.DocType != StockDocType.Opening) { res.Errors.Add(new() { Field = "DocType", Message = "Invalid document type." }); return res; }

            // Preload items by SKU
            var skus = lines.Select(l => l.Sku.Trim()).Where(s => s.Length > 0).Distinct().ToList();
            var items = await db.Items.Where(i => skus.Contains(i.Sku)).Select(i => new { i.Id, i.Sku }).ToListAsync(ct);
            var itemBySku = items.ToDictionary(x => x.Sku, x => x.Id, StringComparer.OrdinalIgnoreCase);

            int row = 0;
            foreach (var l in lines)
            {
                var sku = (l.Sku ?? "").Trim();
                if (string.IsNullOrWhiteSpace(sku))
                    res.Errors.Add(new() { RowIndex = row, Field = "Sku", Message = "SKU is required." });

                if (!itemBySku.ContainsKey(sku))
                    res.Errors.Add(new() { RowIndex = row, Field = "Sku", Sku = sku, Message = "SKU not found. Create item first." });

                if (l.Qty <= 0)
                    res.Errors.Add(new() { RowIndex = row, Field = "Qty", Sku = sku, Message = "Quantity must be > 0." });

                // UnitCost mandatory with 4dp allowed; EF precision handled in model.
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
            if (doc.Status != StockDocStatus.Draft) throw new InvalidOperationException("Document is locked and cannot be edited.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");

            // Validate rows
            var val = await ValidateLinesAsync(req.StockDocId, req.Lines, ct);
            if (!val.Ok)
            {
                var first = val.Errors.First();
                throw new InvalidOperationException($"Validation failed at row {(first.RowIndex ?? -1)} ({first.Field}): {first.Message}");
            }

            // Map SKUs to Items
            var skus = req.Lines.Select(l => l.Sku.Trim()).Distinct().ToList();
            var items = await db.Items.Where(i => skus.Contains(i.Sku)).Select(i => new { i.Id, i.Sku }).ToListAsync(ct);
            var itemBySku = items.ToDictionary(x => x.Sku, x => x.Id, StringComparer.OrdinalIgnoreCase);

            // Optionally replace existing lines
            if (req.ReplaceAll)
            {
                var existing = db.StockEntries.Where(se => se.StockDocId == doc.Id);
                db.StockEntries.RemoveRange(existing);
            }

            // Insert (or merge by SKU)
            // For merge, we remove existing same item line and insert the new one—simple rule for now.
            var existingByItem = await db.StockEntries.Where(se => se.StockDocId == doc.Id)
                .ToListAsync(ct);

            foreach (var line in req.Lines)
            {
                var itemId = itemBySku[line.Sku.Trim()];

                if (!req.ReplaceAll)
                {
                    var dup = existingByItem.FirstOrDefault(x => x.ItemId == itemId);
                    if (dup != null) db.StockEntries.Remove(dup);
                }

                db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = itemId,
                    QtyChange = line.Qty,           // decimal(18,4)
                    UnitCost = line.UnitCost,       // decimal(18,4)
                    LocationType = doc.LocationType,
                    LocationId = doc.LocationId,
                    RefType = "Opening",
                    RefId = doc.Id,
                    Ts = doc.EffectiveDateUtc,
                    Note = line.Note ?? ""
                });
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task LockAsync(int stockDocId, int adminUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var doc = await db.StockDocs
                .Include(d => d.Lines) // Lines = StockEntries
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);

            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status == StockDocStatus.Locked) { await tx.CommitAsync(ct); return; } // idempotent

            if (doc.Lines.Count == 0) throw new InvalidOperationException("No lines to lock.");
            if (doc.Lines.Any(l => l.UnitCost < 0 || l.QtyChange <= 0))
                throw new InvalidOperationException("Invalid line values.");

            // Safety: re-run your negative-forward guard
            await GuardNoNegativeForwardAsync(db, doc, ct);

            // Normalize all child StockEntries to the header values (in case date/location changed)
#if EF_CORE_7_OR_LATER
    await db.StockEntries
        .Where(se => se.StockDocId == doc.Id)
        .ExecuteUpdateAsync(s => s
            .SetProperty(se => se.Ts,           _ => doc.EffectiveDateUtc)
            .SetProperty(se => se.LocationType, _ => doc.LocationType)
            .SetProperty(se => se.LocationId,   _ => doc.LocationId)
            .SetProperty(se => se.RefType,      _ => "Opening")
            .SetProperty(se => se.RefId,        _ => doc.Id),
            ct);
#else
            var children = await db.StockEntries.Where(se => se.StockDocId == doc.Id).ToListAsync(ct);
            foreach (var se in children)
            {
                se.Ts = doc.EffectiveDateUtc;
                se.LocationType = doc.LocationType;
                se.LocationId = doc.LocationId;
                se.RefType = "Opening";
                se.RefId = doc.Id;
            }
#endif

            // Lock header
            doc.Status = StockDocStatus.Locked;
            doc.LockedByUserId = adminUserId;
            doc.LockedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }


        public async Task UnlockAsync(int stockDocId, int adminUserId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var doc = await db.StockDocs
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == stockDocId, ct);

            if (doc == null) throw new InvalidOperationException("Opening document not found.");
            if (doc.DocType != StockDocType.Opening) throw new InvalidOperationException("Invalid document type.");
            if (doc.Status != StockDocStatus.Locked) { await tx.CommitAsync(ct); return; } // idempotent

            // Optional: forbid unlock if later movements exist (safe mode)
            // (keep commented if you want free unlocks)
            // var hasLater = await db.StockEntries.AnyAsync(se =>
            //     se.Ts > doc.EffectiveDateUtc &&
            //     se.ItemId == se.ItemId && // placeholder, refine by affected items+location if needed
            //     !("Opening".Equals(se.RefType) && se.RefId == doc.Id), ct);
            // if (hasLater) throw new InvalidOperationException("Cannot unlock: later movements exist.");

            doc.Status = StockDocStatus.Draft;
            doc.LockedByUserId = null;
            doc.LockedAtUtc = null;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
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

        /// <summary>
        /// Ensure that applying this Opening stock (which is already in Lines with EffectiveDate)
        /// never causes negative on-hand for any affected Item at Location AFTER EffectiveDate.
        /// We simulate forward using chronological StockEntry for the affected items+location.
        /// </summary>
        private static async Task GuardNoNegativeForwardAsync(PosClientDbContext db, StockDoc doc, CancellationToken ct)
        {
            var locType = doc.LocationType;
            var locId = doc.LocationId;
            var eff = doc.EffectiveDateUtc;

            var itemIds = doc.Lines.Select(l => l.ItemId).Distinct().ToList();

            // Load all movements for affected items at the same location from EffectiveDate onward.
            // Include the opening lines (already attached to doc) and all other entries.
            var laterMovements = await db.StockEntries.AsNoTracking()
                .Where(se => se.LocationType == locType && se.LocationId == locId
                             && itemIds.Contains(se.ItemId)
                             && se.Ts >= eff)
                .Select(se => new { se.ItemId, se.QtyChange, se.Ts, se.RefType, se.RefId })
                .OrderBy(se => se.Ts).ThenBy(se => se.RefType).ThenBy(se => se.RefId)
                .ToListAsync(ct);

            // Seed at eff with the sum of this document's opening qty per item.
            var openingByItem = doc.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyChange));

            // We need any movements occurring exactly at eff but NOT belonging to this doc to be applied after opening.
            // Our ordering by (Ts, RefType, RefId) will naturally place "Opening" first if needed; if not guaranteed,
            // we can enforce it by adjusting the ThenBy to make RefType="Opening" come first. Simpler: just accumulate manually.

            foreach (var itemId in itemIds)
            {
                decimal running = openingByItem.TryGetValue(itemId, out var init) ? init : 0m;

                foreach (var mv in laterMovements.Where(m => m.ItemId == itemId))
                {
                    // If this is one of our opening lines, skip (already seeded).
                    if (mv.RefType == "Opening" && mv.RefId == doc.Id && mv.Ts == eff)
                        continue;

                    running += mv.QtyChange;

                    if (running < 0)
                        throw new InvalidOperationException($"Negative stock detected for ItemId={itemId} after {mv.Ts:u}.");
                }
            }
        }
    }
}
