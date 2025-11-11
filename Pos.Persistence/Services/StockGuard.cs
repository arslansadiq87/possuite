// Pos.Persistence/Services/StockGuard.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Inventory;
using Pos.Domain.Services;
using Pos.Persistence;

namespace Pos.Persistence.Services
{
    /// <inheritdoc/>
    public sealed class StockGuard : IStockGuard
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public StockGuard(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        public async Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.StockEntries.AsNoTracking()
                .Where(e => e.ItemId == itemId
                            && e.LocationType == InventoryLocationType.Outlet
                            && e.LocationId == outletId)
                .SumAsync(e => e.QtyChange, ct);
        }

        public async Task<decimal> GetOnHandAtLocationAsync(
            int itemId,
            int outletId, // not used for filtering; preserved for API compatibility / message context
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.StockEntries.AsNoTracking()
                .Where(e => e.ItemId == itemId
                            && e.LocationType == locType
                            && e.LocationId == locId);

            if (atUtc.HasValue) q = q.Where(e => e.Ts <= atUtc.Value);

            return await q.SumAsync(e => e.QtyChange, ct);
        }

        public async Task EnsureNoNegativeAtLocationAsync(
            IEnumerable<StockDeltaDto> deltas,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            if (deltas is null) return;

            // Snapshot & group (OutletId retained for message clarity)
            var grouped = deltas
                .GroupBy(d => new { d.ItemId, d.OutletId, d.LocType, d.LocId })
                .Select(g => new
                {
                    g.Key.ItemId,
                    g.Key.OutletId,
                    g.Key.LocType,
                    g.Key.LocId,
                    Delta = g.Sum(x => x.Delta)
                })
                .ToList();

            if (grouped.Count == 0) return;

            var itemIds = grouped.Select(x => x.ItemId).Distinct().ToList();
            var locTypes = grouped.Select(x => x.LocType).Distinct().ToList();
            var locIds = grouped.Select(x => x.LocId).Distinct().ToList();

            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Balances by (ItemId, LocationType, LocationId) ONLY
            var balancesQ = db.StockEntries.AsNoTracking()
                .Where(e => itemIds.Contains(e.ItemId)
                            && locTypes.Contains(e.LocationType)
                            && locIds.Contains(e.LocationId));

            if (atUtc.HasValue) balancesQ = balancesQ.Where(e => e.Ts <= atUtc.Value);

            var balances = await balancesQ
                .GroupBy(e => new { e.ItemId, e.LocationType, e.LocationId })
                .Select(g => new
                {
                    g.Key.ItemId,
                    g.Key.LocationType,
                    g.Key.LocationId,
                    OnHand = g.Sum(x => x.QtyChange)
                })
                .ToListAsync(ct);

            foreach (var g in grouped)
            {
                var onHand = balances
                    .Where(b => b.ItemId == g.ItemId
                                && b.LocationType == g.LocType
                                && b.LocationId == g.LocId)
                    .Select(b => b.OnHand)
                    .DefaultIfEmpty(0m)
                    .Single();

                if (onHand + g.Delta < 0m)
                {
                    // OutletId shown for context; matching was by (LocType, LocId).
                    throw new InvalidOperationException(
                        $"Negative stock for Item#{g.ItemId} at Outlet#{g.OutletId} {g.LocType}#{g.LocId} " +
                        $"(on-hand {onHand:0.####}, delta {g.Delta:0.####}).");
                }
            }
        }
    }
}
