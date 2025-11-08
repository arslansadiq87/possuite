using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain;
using Pos.Domain.Settings;
using Pos.Persistence.Services;
using Pos.Persistence.Sync;   // NEW

namespace Pos.Persistence.Features.Transfers
{
    public sealed class TransferService : ITransferService
    {
        private readonly Pos.Persistence.PosClientDbContext _db;
        private readonly StockGuard _guard; // add this
        private readonly IOutboxWriter _outbox;   // NEW

        public TransferService(PosClientDbContext db, StockGuard guard, IOutboxWriter outbox)
        {
            _db = db;
            _guard = guard;
            _outbox = outbox;
        }

        public TransferService(PosClientDbContext db, IOutboxWriter outbox)
        {
            _db = db;
            _guard = new StockGuard(db);
            _outbox = outbox;
        }


        private static bool IsAdminOrManager(User u) =>
    u.IsGlobalAdmin || u.Role == UserRole.Admin || u.Role == UserRole.Manager;

        private static void EnsureCanEditTransfer(User user, StockDoc doc)
        {
            if (IsAdminOrManager(user)) return;
            if (user.Id == doc.CreatedByUserId) return; // sender can edit own draft
            throw new InvalidOperationException("You do not have permission to edit this transfer.");
        }


        public async Task<StockDoc> UndoDispatchAsync(int stockDocId, DateTime effectiveDateUtc, int actedByUserId, string? reason = null)
        {
            var user = await GetUserAsync(actedByUserId);
            EnsureCanManageTransfers(user);
            var doc = await _db.StockDocs
                               .Include(d => d.TransferLines)
                               .FirstOrDefaultAsync(d => d.Id == stockDocId)
                      ?? throw new InvalidOperationException("Transfer not found.");
            EnsureDocIs(doc, StockDocType.Transfer);
            if (doc.TransferStatus != TransferStatus.Dispatched)
                throw new InvalidOperationException("Only Dispatched transfers can be undone.");
            if (doc.ReceivedAtUtc.HasValue || doc.TransferStatus == TransferStatus.Received)
                throw new InvalidOperationException("Cannot undo: transfer already received.");
            // Reverse the OUT ledger that was posted at dispatch (audit-friendly)
            var ts = effectiveDateUtc.ToUniversalTime();
            // Create OUT ledger rows and snapshot costs onto TransferLines
            foreach (var line in doc.TransferLines)
            {
                var cost = await ComputeMovingAverageCostAsync(
                    line.ItemId, doc.LocationType, doc.LocationId, ts);
                line.UnitCostExpected = cost; // snapshot for valuation & receive
                _db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = line.ItemId,
                    LocationType = doc.LocationType,
                    LocationId = doc.LocationId,
                    QtyChange = -line.QtyExpected,   // OUT on dispatch
                    UnitCost = cost,
                    RefType = "TransferOut",
                    RefId = doc.Id,
                    Ts = ts,
                    Note = line.Remarks
                });
            }



            // Back to Draft so user can edit / re-dispatch
            doc.TransferStatus = TransferStatus.Draft;
            doc.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // === SYNC: dispatch undone → back to Draft ===
            await _outbox.EnqueueUpsertAsync(_db, doc, default);
            await _db.SaveChangesAsync();

            return doc;

        }

        public async Task<StockDoc> UndoReceiveAsync(int stockDocId, DateTime effectiveDateUtc, int actedByUserId, string? reason = null)
        {
            var user = await GetUserAsync(actedByUserId);
            EnsureCanManageTransfers(user);

            var doc = await _db.StockDocs
                               .Include(d => d.TransferLines)
                               .FirstOrDefaultAsync(d => d.Id == stockDocId)
                      ?? throw new InvalidOperationException("Transfer not found.");

            EnsureDocIs(doc, StockDocType.Transfer);

            if (doc.TransferStatus != TransferStatus.Received || !doc.ReceivedAtUtc.HasValue)
                throw new InvalidOperationException("Only Received transfers can be undone.");

            var ts = effectiveDateUtc.ToUniversalTime();

            // 1) Guard: ensure removing the received qty won't make 'To' stock negative
            // Compute, per item, the quantities we will remove (primary + overage).
            var requiredRemovals = doc.TransferLines
                .Select(l =>
                {
                    var rec = l.QtyReceived ?? 0m;
                    var exp = l.QtyExpected;
                    var primary = Math.Min(rec, exp);
                    var over = Math.Max(rec - exp, 0m);
                    return new { l.ItemId, RemoveQty = primary + over };
                })
                .Where(x => x.RemoveQty > 0m)
                .ToList();

            if (requiredRemovals.Count > 0)
            {
                foreach (var r in requiredRemovals)
                {
                    var onHandTo = await _db.StockEntries.AsNoTracking()
                        .Where(se => se.ItemId == r.ItemId
                                     && se.LocationType == doc.ToLocationType!.Value
                                     && se.LocationId == doc.ToLocationId!.Value
                                     && se.Ts <= ts)
                        .SumAsync(se => (decimal?)se.QtyChange) ?? 0m;

                    if (onHandTo - r.RemoveQty < 0m)
                        throw new InvalidOperationException(
                            $"Cannot undo receive: insufficient stock at destination for ItemId {r.ItemId}. " +
                            $"On-hand {onHandTo:0.####}, need to remove {r.RemoveQty:0.####}.");
                }
            }

            // 2) Add compensating OUT entries at the To location (reverse the earlier INs)
            foreach (var line in doc.TransferLines)
            {
                var rec = line.QtyReceived ?? 0m;
                if (rec <= 0m) continue;

                var exp = line.QtyExpected;
                var primary = Math.Min(rec, exp);
                var over = Math.Max(rec - exp, 0m);

                if (primary > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = -primary,                         // reverse the IN
                        UnitCost = line.UnitCostExpected ?? 0m,
                        RefType = "TransferInUndo",
                        RefId = doc.Id,
                        Ts = ts,
                        Note = string.IsNullOrWhiteSpace(reason) ? "Undo receive" : $"Undo receive: {reason}"
                    });
                }

                if (over > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = -over,                            // reverse the overage
                        UnitCost = line.UnitCostExpected ?? 0m,
                        RefType = "TransferOverageUndo",
                        RefId = doc.Id,
                        Ts = ts,
                        Note = "Undo overage on receive"
                    });
                }

                // Clear receive markers so it can be received again
                line.QtyReceived = null;
                line.VarianceNote = null;
            }

            // 3) Header back to Dispatched
            doc.TransferStatus = TransferStatus.Dispatched;
            doc.ReceivedAtUtc = null;
            doc.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // === SYNC: receive undone → back to Dispatched ===
            await _outbox.EnqueueUpsertAsync(_db, doc, default);
            await _db.SaveChangesAsync();

            return doc;

        }



        // -------- Helpers ----------------------------------------------------


        private static void EnsureCanManageTransfers(User? user)
        {
            if (user is null || !(user.Role == UserRole.Manager || user.Role == UserRole.Admin || user.IsGlobalAdmin))
                throw new InvalidOperationException("You do not have permission to manage stock transfers.");
        }

        private async Task<User> GetUserAsync(int userId)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (u == null) throw new InvalidOperationException("User not found.");
            return u;
        }

        private static void ValidateDraftHeader(InventoryLocationType fromType, int fromId, InventoryLocationType toType, int toId)
        {
            if (fromId <= 0 || toId <= 0) throw new InvalidOperationException("From/To location not selected.");
            if (fromType == toType && fromId == toId) throw new InvalidOperationException("From and To cannot be the same.");
        }

        private static void ValidateLinesInput(IEnumerable<TransferLineDto> lines)
        {
            var list = lines?.ToList() ?? throw new InvalidOperationException("Lines are required.");
            if (list.Count == 0) throw new InvalidOperationException("At least one line is required.");
            if (list.Any(l => l.ItemId <= 0)) throw new InvalidOperationException("Invalid Item.");
            if (list.Any(l => l.QtyExpected <= 0)) throw new InvalidOperationException("Quantity must be > 0.");
        }

        private static void ValidateReceiveInput(IEnumerable<ReceiveLineDto> lines)
        {
            var list = lines?.ToList() ?? throw new InvalidOperationException("Receive lines are required.");
            if (list.Count == 0) throw new InvalidOperationException("At least one line is required.");
            if (list.Any(l => l.LineId <= 0)) throw new InvalidOperationException("Invalid line id.");
            if (list.Any(l => l.QtyReceived < 0)) throw new InvalidOperationException("Received quantity cannot be negative.");
        }

        private async Task<string> ResolveLocationCodeAsync(InventoryLocationType type, int id)
        {
            if (type == InventoryLocationType.Outlet)
            {
                var o = await _db.Outlets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                        ?? throw new InvalidOperationException("Source outlet not found.");
                return string.IsNullOrWhiteSpace(o.Code) ? $"LOC{o.Id}" : o.Code.Trim();
            }
            else
            {
                var w = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                        ?? throw new InvalidOperationException("Source warehouse not found.");
                return string.IsNullOrWhiteSpace(w.Code) ? $"LOC{w.Id}" : w.Code.Trim();
            }
        }

        private static int YearOf(DateTime utc) => utc.ToUniversalTime().Year;

        private async Task<string> GenerateTransferNoAsync(StockDoc doc, DateTime effectiveUtc)
        {
            var fromCode = await ResolveLocationCodeAsync(doc.LocationType, doc.LocationId);
            var year = YearOf(effectiveUtc);

            var existingCount = await _db.StockDocs
                .AsNoTracking()
                .Where(d => d.DocType == StockDocType.Transfer
                            && d.LocationType == doc.LocationType
                            && d.LocationId == doc.LocationId
                            && d.EffectiveDateUtc.Year == year)
                .CountAsync();

            var next = existingCount + 1;
            return $"TR-{fromCode}-{year}-{next:D5}";
        }

        private async Task<decimal> ComputeMovingAverageCostAsync(int itemId, InventoryLocationType type, int locId, DateTime atUtc)
        {
            var ins = await _db.StockEntries.AsNoTracking()
                .Where(se => se.ItemId == itemId
                             && se.LocationType == type
                             && se.LocationId == locId
                             && se.Ts <= atUtc
                             && se.QtyChange > 0)
                .Select(se => new { se.QtyChange, se.UnitCost })
                .ToListAsync();

            if (ins.Count == 0) return 0m;

            decimal totalQty = 0m, totalCost = 0m;
            foreach (var x in ins)
            {
                totalQty += x.QtyChange;
                totalCost += x.QtyChange * x.UnitCost;
            }
            return totalQty == 0m ? 0m : Math.Round(totalCost / totalQty, 4, MidpointRounding.AwayFromZero);
        }

        private async Task EnsureNoNegativeAtDispatchAsync(StockDoc draft, DateTime effectiveUtc, IReadOnlyList<StockDocLine> lines)
        {
            var fromType = draft.LocationType;
            var fromId = draft.LocationId;

            var linesByItem = lines.GroupBy(l => l.ItemId)
                                   .Select(g => new { ItemId = g.Key, OutQty = g.Sum(x => x.QtyExpected) })
                                   .ToList();

            foreach (var g in linesByItem)
            {
                var cutoffUtc = effectiveUtc.Date.AddDays(1); // include entire selected day
                var qty = await _db.StockEntries.AsNoTracking()
                    .Where(se => se.ItemId == g.ItemId
                                 && se.LocationType == fromType
                                 && se.LocationId == fromId
                                 && se.Ts < cutoffUtc)   // exclusive upper bound = next day 00:00
                    .SumAsync(se => se.QtyChange);

                if (qty - g.OutQty < 0m)
                    throw new InvalidOperationException($"Negative stock risk for ItemId {g.ItemId}. On-hand {qty:0.####}, trying to send {g.OutQty:0.####}.");
            }
        }

        private static void EnsureDocIs(StockDoc d, StockDocType t)
        {
            if (d.DocType != t) throw new InvalidOperationException("Document type mismatch.");
        }

        // -------- API --------------------------------------------------------

        public async Task<StockDoc> CreateDraftAsync(
    InventoryLocationType fromType, int fromId,
    InventoryLocationType toType, int toId,
    DateTime effectiveDateUtc,
    int createdByUserId)
        {
            var user = await GetUserAsync(createdByUserId);
            EnsureCanManageTransfers(user);
            ValidateDraftHeader(fromType, fromId, toType, toId);

            var doc = new StockDoc
            {
                DocType = StockDocType.Transfer,
                LocationType = fromType,
                LocationId = fromId,
                ToLocationType = toType,
                ToLocationId = toId,
                EffectiveDateUtc = effectiveDateUtc.ToUniversalTime(),
                TransferStatus = TransferStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = user.Id,                // NEW
                AutoReceiveOnDispatch = false             // default
            };

            _db.StockDocs.Add(doc);
            await _db.SaveChangesAsync();
            return doc;
        }


        public async Task<StockDoc> UpsertLinesAsync(int stockDocId, IReadOnlyList<TransferLineDto> lines, bool replaceAll)
        {
            ValidateLinesInput(lines);

            var doc = await _db.StockDocs
                               .Include(d => d.TransferLines)
                               .FirstOrDefaultAsync(d => d.Id == stockDocId)
                      ?? throw new InvalidOperationException("Transfer not found.");

            EnsureDocIs(doc, StockDocType.Transfer);

            var user = await GetUserAsync(doc.CreatedByUserId);
            EnsureCanEditTransfer(user, doc);            // NEW enforce author OR admin/manager

            if (doc.TransferStatus != TransferStatus.Draft)
                throw new InvalidOperationException("Only Draft transfers can be edited.");

            if (replaceAll)
            {
                _db.StockDocLines.RemoveRange(doc.TransferLines);
                doc.TransferLines.Clear();
            }

            var byItem = doc.TransferLines.ToDictionary(l => l.ItemId, l => l);
            foreach (var l in lines)
            {
                if (byItem.TryGetValue(l.ItemId, out var existing))
                {
                    existing.QtyExpected = l.QtyExpected;
                    existing.Remarks = l.Remarks;
                }
                else
                {
                    var item = await _db.Items.AsNoTracking()
                                              .FirstOrDefaultAsync(i => i.Id == l.ItemId)
                               ?? throw new InvalidOperationException($"Item {l.ItemId} not found.");

                    doc.TransferLines.Add(new StockDocLine
                    {
                        ItemId = l.ItemId,
                        SkuSnapshot = item.Sku ?? "",
                        ItemNameSnapshot = item.Name ?? "",
                        QtyExpected = l.QtyExpected,
                        Remarks = l.Remarks
                    });
                }
            }

            await _db.SaveChangesAsync();
            return doc;
        }

        public async Task<StockDoc> DispatchAsync(int docId, DateTime effectiveUtc, int userId, bool autoReceive)
        {
            using var tx = await _db.Database.BeginTransactionAsync();

            var actor = await GetUserAsync(userId);
            EnsureCanManageTransfers(actor);

            var doc = await _db.StockDocs
                .Include(d => d.TransferLines)
                .FirstOrDefaultAsync(d => d.Id == docId)
                ?? throw new InvalidOperationException("Transfer not found.");

            EnsureDocIs(doc, StockDocType.Transfer);
            if (doc.TransferStatus != TransferStatus.Draft)
                throw new InvalidOperationException("Only Draft transfers can be dispatched.");

            if (doc.LocationType == null || doc.ToLocationType == null || doc.LocationId == null || doc.ToLocationId == null)
                throw new InvalidOperationException("From/To not set.");

            var fromType = doc.LocationType;
            var fromId = doc.LocationId;
            var toType = doc.ToLocationType.Value;
            var toId = doc.ToLocationId.Value;

            var lines = doc.TransferLines.ToList();
            if (lines.Count == 0) throw new InvalidOperationException("No lines to dispatch.");

            // 1) Guard: no negative at source on the selected date
            var deltas = lines
                .Where(l => l.QtyExpected > 0m)
                .Select(l => (
                    itemId: l.ItemId,
                    outletId: (fromType == InventoryLocationType.Outlet) ? fromId : 0, // outlet aggregate if you track one
                    locType: fromType,
                    locId: fromId,
                    delta: -l.QtyExpected))
                .ToArray();

            await _guard.EnsureNoNegativeAtLocationAsync(deltas, atUtc: effectiveUtc);

            // 2) Ledger entries
            var entries = new List<StockEntry>();

            foreach (var l in lines)
            {
                if (l.QtyExpected <= 0m) continue;

                // OUT at source
                entries.Add(new StockEntry
                {
                    OutletId = (fromType == InventoryLocationType.Outlet) ? fromId : 0,
                    ItemId = l.ItemId,
                    QtyChange = -l.QtyExpected,
                    UnitCost = l.UnitCostExpected ?? 0m,
                    LocationType = fromType,
                    LocationId = fromId,
                    StockDocId = doc.Id,
                    RefType = "TransferOut",
                    RefId = doc.Id,
                    Ts = effectiveUtc,
                    Note = l.Remarks
                });

                if (autoReceive)
                {
                    // IN at destination immediately
                    entries.Add(new StockEntry
                    {
                        OutletId = (toType == InventoryLocationType.Outlet) ? toId : 0,
                        ItemId = l.ItemId,
                        QtyChange = l.QtyExpected,
                        UnitCost = l.UnitCostExpected ?? 0m,
                        LocationType = toType,
                        LocationId = toId,
                        StockDocId = doc.Id,
                        RefType = "TransferIn",
                        RefId = doc.Id,
                        Ts = effectiveUtc,
                        Note = l.Remarks
                    });

                    l.QtyReceived = l.QtyExpected; // snapshot for print/history
                }
            }

            _db.StockEntries.AddRange(entries);

            // 3) Stamp status & flags
            doc.EffectiveDateUtc = effectiveUtc;
            doc.AutoReceiveOnDispatch = autoReceive;
            if (autoReceive)
            {
                doc.TransferStatus = TransferStatus.Received;
                doc.ReceivedAtUtc = effectiveUtc;
            }
            else
            {
                doc.TransferStatus = TransferStatus.Dispatched;
                doc.ReceivedAtUtc = null;
            }
            doc.UpdatedAtUtc = DateTime.UtcNow;

            // Persist transfer header/lines/entries
            await _db.SaveChangesAsync();

            // === SYNC: transfer state finalized to Dispatched (or Received if autoReceive) ===
            await _outbox.EnqueueUpsertAsync(_db, doc, default);
            await _db.SaveChangesAsync();          // persist outbox row in the SAME transaction

            await tx.CommitAsync();
            return doc;

        }


        public async Task<StockDoc> ReceiveAsync(int stockDocId, DateTime receivedAtUtc, IReadOnlyList<ReceiveLineDto> lines, int actedByUserId)
        {
            var user = await GetUserAsync(actedByUserId);
            EnsureCanManageTransfers(user);
            ValidateReceiveInput(lines);

            var doc = await _db.StockDocs
                               .Include(d => d.TransferLines)
                               .FirstOrDefaultAsync(d => d.Id == stockDocId)
                      ?? throw new InvalidOperationException("Transfer not found.");

            EnsureDocIs(doc, StockDocType.Transfer);
            if (doc.TransferStatus != TransferStatus.Dispatched)
                throw new InvalidOperationException("Only Dispatched transfers can be received.");

            var ts = receivedAtUtc.ToUniversalTime();
            if (ts < doc.EffectiveDateUtc)
                throw new InvalidOperationException("Receive date cannot be earlier than dispatch date.");

            var map = doc.TransferLines.ToDictionary(l => l.Id, l => l);

            foreach (var r in lines)
            {
                if (!map.TryGetValue(r.LineId, out var line))
                    throw new InvalidOperationException($"Line {r.LineId} not found.");

                // persist user input on the doc line
                line.QtyReceived = r.QtyReceived;
                line.VarianceNote = r.VarianceNote;

                // split received into primary (up to expected) and overage (beyond expected)
                var inQty = Math.Max(r.QtyReceived, 0m);
                var expect = Math.Max(line.QtyExpected, 0m);
                var primary = Math.Min(inQty, expect);
                var over = Math.Max(inQty - expect, 0m);

                if (primary > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = primary,                               // IN up to expected
                        UnitCost = line.UnitCostExpected ?? 0m,
                        RefType = "TransferIn",
                        RefId = doc.Id,
                        Ts = ts,
                        Note = line.VarianceNote
                    });
                }

                if (over > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = over,                                  // IN beyond expected
                        UnitCost = line.UnitCostExpected ?? 0m,
                        RefType = "TransferOverage",
                        RefId = doc.Id,
                        Ts = ts,
                        Note = "Overage on receive"
                    });
                }
            }

            doc.TransferStatus = TransferStatus.Received;
            doc.ReceivedAtUtc = ts;
            doc.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // === SYNC: transfer received (primary + any overage recorded) ===
            await _outbox.EnqueueUpsertAsync(_db, doc, default);
            await _db.SaveChangesAsync();

            return doc;

        }


        public Task<StockDoc?> GetAsync(int stockDocId)
            => _db.StockDocs
                  .Include(d => d.TransferLines)
                  .FirstOrDefaultAsync(d => d.Id == stockDocId);


    }
}
