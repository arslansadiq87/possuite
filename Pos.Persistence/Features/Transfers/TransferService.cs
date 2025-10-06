using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain;

namespace Pos.Persistence.Features.Transfers
{
    public sealed class TransferService : ITransferService
    {
        private readonly Pos.Persistence.PosClientDbContext _db;

        public TransferService(Pos.Persistence.PosClientDbContext db) => _db = db;

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
                var qty = await _db.StockEntries.AsNoTracking()
                    .Where(se => se.ItemId == g.ItemId
                                 && se.LocationType == fromType
                                 && se.LocationId == fromId
                                 && se.Ts <= effectiveUtc)
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
                CreatedAtUtc = DateTime.UtcNow
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

        public async Task<StockDoc> DispatchAsync(int stockDocId, DateTime effectiveDateUtc, int actedByUserId)
        {
            var user = await GetUserAsync(actedByUserId);
            EnsureCanManageTransfers(user);

            var doc = await _db.StockDocs
                               .Include(d => d.TransferLines)
                               .FirstOrDefaultAsync(d => d.Id == stockDocId)
                      ?? throw new InvalidOperationException("Transfer not found.");

            EnsureDocIs(doc, StockDocType.Transfer);
            if (doc.TransferStatus != TransferStatus.Draft)
                throw new InvalidOperationException("Only Draft transfers can be dispatched.");
            if (doc.TransferLines.Count == 0)
                throw new InvalidOperationException("Add at least one line before dispatch.");

            var ts = effectiveDateUtc.ToUniversalTime();

            // NEGATIVE GUARD using TransferLines
            await EnsureNoNegativeAtDispatchAsync(doc, ts, doc.TransferLines.ToList());

            // Numbering per-From + year
            if (string.IsNullOrWhiteSpace(doc.TransferNo))
                doc.TransferNo = await GenerateTransferNoAsync(doc, ts);

            // Create OUT ledger rows and snapshot costs onto TransferLines
            foreach (var line in doc.TransferLines)
            {
                var cost = await ComputeMovingAverageCostAsync(line.ItemId, doc.LocationType, doc.LocationId, ts);
                line.UnitCostExpected = cost;

                _db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = line.ItemId,
                    LocationType = doc.LocationType,
                    LocationId = doc.LocationId,
                    QtyChange = -line.QtyExpected,
                    UnitCost = cost,
                    RefType = "TransferOut",
                    RefId = doc.Id,
                    Ts = ts,
                    Note = line.Remarks
                });
            }

            doc.TransferStatus = TransferStatus.Dispatched;
            doc.EffectiveDateUtc = ts;
            doc.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
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

                line.QtyReceived = r.QtyReceived;
                line.VarianceNote = r.VarianceNote;

                var inQty = r.QtyReceived;

                // Primary IN (can be zero)
                if (inQty > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = inQty,
                        UnitCost = line.UnitCostExpected ?? 0m,
                        RefType = "TransferIn",
                        RefId = doc.Id,
                        Ts = ts,
                        Note = line.VarianceNote
                    });
                }

                // Overage: extra IN
                var over = Math.Max(inQty - line.QtyExpected, 0m);
                if (over > 0m)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        StockDocId = doc.Id,
                        ItemId = line.ItemId,
                        LocationType = doc.ToLocationType!.Value,
                        LocationId = doc.ToLocationId!.Value,
                        QtyChange = over,
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
            return doc;
        }

        public Task<StockDoc?> GetAsync(int stockDocId)
            => _db.StockDocs
                  .Include(d => d.TransferLines)
                  .FirstOrDefaultAsync(d => d.Id == stockDocId);
    }
}
