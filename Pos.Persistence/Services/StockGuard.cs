// Pos.Persistence/Services/StockGuard.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Central negative-stock guard. Reusable by all services/UIs.
    /// </summary>
    public sealed class StockGuard
    {
        private readonly PosClientDbContext _db;
        public StockGuard(PosClientDbContext db) => _db = db;

        public Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default) =>
            _db.StockEntries.AsNoTracking()
               .Where(e => e.ItemId == itemId && e.OutletId == outletId)
               .SumAsync(e => e.QtyChange, ct);

        public Task<decimal> GetOnHandAtLocationAsync(
            int itemId, int outletId, InventoryLocationType locType, int locId, DateTime? atUtc = null, CancellationToken ct = default)
        {
            var q = _db.StockEntries.AsNoTracking()
                .Where(e => e.ItemId == itemId
                            && e.OutletId == outletId
                            && e.LocationType == locType
                            && e.LocationId == locId);

            if (atUtc.HasValue) q = q.Where(e => e.Ts <= atUtc.Value);
            return q.SumAsync(e => e.QtyChange, ct);
        }

        public async Task EnsureNoNegativeAtLocationAsync(
            (int itemId, int outletId, InventoryLocationType locType, int locId, decimal delta)[] deltas,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            if (deltas == null || deltas.Length == 0) return;

            var grouped = deltas
                .GroupBy(d => new { d.itemId, d.outletId, d.locType, d.locId })
                .Select(g => new { g.Key.itemId, g.Key.outletId, g.Key.locType, g.Key.locId, delta = g.Sum(x => x.delta) })
                .ToList();

            var balancesQ = _db.StockEntries.AsNoTracking()
                .Where(e => grouped.Select(x => x.itemId).Contains(e.ItemId)
                            && grouped.Select(x => x.outletId).Contains(e.OutletId)
                            && grouped.Select(x => x.locType).Contains(e.LocationType)
                            && grouped.Select(x => x.locId).Contains(e.LocationId));

            if (atUtc.HasValue) balancesQ = balancesQ.Where(e => e.Ts <= atUtc.Value);

            var balances = await balancesQ
                .GroupBy(e => new { e.ItemId, e.OutletId, e.LocationType, e.LocationId })
                .Select(g => new
                {
                    g.Key.ItemId,
                    g.Key.OutletId,
                    g.Key.LocationType,
                    g.Key.LocationId,
                    OnHand = g.Sum(x => x.QtyChange)
                })
                .ToListAsync(ct);

            foreach (var g in grouped)
            {
                var onHand = balances
                    .Where(b => b.ItemId == g.itemId
                                && b.OutletId == g.outletId
                                && b.LocationType == g.locType
                                && b.LocationId == g.locId)
                    .Select(b => b.OnHand)
                    .DefaultIfEmpty(0m)
                    .Single();

                if (onHand + g.delta < 0m)
                    throw new InvalidOperationException(
                        $"Negative stock for Item#{g.itemId} at Outlet#{g.outletId} {g.locType}#{g.locId} " +
                        $"(on-hand {onHand:0.####}, delta {g.delta:0.####}).");
            }
        }
    }
}
