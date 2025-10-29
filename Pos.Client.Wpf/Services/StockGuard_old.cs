// Pos.Client.Wpf/Services/StockGuard.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Hard guard against negative stock.
    /// Uses StockEntry.QtyChange (+in, -out) and normalized location keys.
    /// </summary>
    public sealed class StockGuard
    {
        private readonly PosClientDbContext _db;
        public StockGuard(PosClientDbContext db) => _db = db;

        // On-hand for an item in an outlet (all locations inside that outlet)
        public Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default) =>
            _db.StockEntries
               .AsNoTracking()
               .Where(e => e.ItemId == itemId && e.OutletId == outletId)
               .SumAsync(e => e.QtyChange, ct);

        // On-hand for an item at a specific normalized location in an outlet
        public Task<decimal> GetOnHandAtLocationAsync(int itemId, int outletId, InventoryLocationType locType, int locId, CancellationToken ct = default) =>
            _db.StockEntries
               .AsNoTracking()
               .Where(e => e.ItemId == itemId
                           && e.OutletId == outletId
                           && e.LocationType == locType
                           && e.LocationId == locId)
               .SumAsync(e => e.QtyChange, ct);

        /// <summary>
        /// Validates a batch of signed deltas at a specific normalized location.
        /// Pass positive for IN, negative for OUT. Throws if any would go < 0.
        /// </summary>
        public async Task EnsureNoNegativeAtLocationAsync(
            (int itemId, int outletId, InventoryLocationType locType, int locId, decimal delta)[] deltas,
            CancellationToken ct = default)
        {
            if (deltas == null || deltas.Length == 0) return;

            // Aggregate per (item, outlet, locType, locId)
            var grouped = deltas
                .GroupBy(d => new { d.itemId, d.outletId, d.locType, d.locId })
                .Select(g => new { g.Key.itemId, g.Key.outletId, g.Key.locType, g.Key.locId, delta = g.Sum(x => x.delta) })
                .ToList();

            // Pull balances for exactly those keys
            var balances = await _db.StockEntries.AsNoTracking()
                .Where(e => grouped.Select(x => x.itemId).Contains(e.ItemId)
                            && grouped.Select(x => x.outletId).Contains(e.OutletId)
                            && grouped.Select(x => x.locType).Contains(e.LocationType)
                            && grouped.Select(x => x.locId).Contains(e.LocationId))
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
                        $"Negative stock for Item#{g.itemId} at Outlet#{g.outletId} {g.locType}#{g.locId} (on-hand {onHand}, delta {g.delta}).");
            }
        }

        /// <summary>
        /// Validates a batch at the outlet aggregate level (all locations inside outlet).
        /// </summary>
        public async Task EnsureNoNegativeAtOutletAsync(
            (int itemId, int outletId, decimal delta)[] deltas,
            CancellationToken ct = default)
        {
            if (deltas == null || deltas.Length == 0) return;

            var grouped = deltas
                .GroupBy(d => new { d.itemId, d.outletId })
                .Select(g => new { g.Key.itemId, g.Key.outletId, delta = g.Sum(x => x.delta) })
                .ToList();

            var balances = await _db.StockEntries.AsNoTracking()
                .Where(e => grouped.Select(x => x.itemId).Contains(e.ItemId)
                            && grouped.Select(x => x.outletId).Contains(e.OutletId))
                .GroupBy(e => new { e.ItemId, e.OutletId })
                .Select(g => new { g.Key.ItemId, g.Key.OutletId, OnHand = g.Sum(x => x.QtyChange) })
                .ToListAsync(ct);

            foreach (var g in grouped)
            {
                var onHand = balances
                    .Where(b => b.ItemId == g.itemId && b.OutletId == g.outletId)
                    .Select(b => b.OnHand)
                    .DefaultIfEmpty(0m)
                    .Single();

                if (onHand + g.delta < 0m)
                    throw new InvalidOperationException(
                        $"Negative stock for Item#{g.itemId} at Outlet#{g.outletId} (on-hand {onHand}, delta {g.delta}).");
            }
        }
    }
}
