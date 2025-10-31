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
    /// IMPORTANT: On-hand is computed by (LocationType, LocationId) ONLY,
    /// to match StockReport and avoid discrepancies from missing OutletId on older rows.
    /// </summary>
    public sealed class StockGuard
    {
        private readonly PosClientDbContext _db;
        public StockGuard(PosClientDbContext db) => _db = db;

        /// <summary>
        /// Convenience: on-hand at an OUTLET location (by outletId).
        /// Uses (LocationType=Outlet, LocationId=outletId). Does NOT filter by OutletId column.
        /// </summary>
        public Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default) =>
            _db.StockEntries.AsNoTracking()
               .Where(e => e.ItemId == itemId
                        && e.LocationType == InventoryLocationType.Outlet
                        && e.LocationId == outletId)
               .SumAsync(e => e.QtyChange, ct);

        /// <summary>
        /// On-hand at an arbitrary location (Outlet or Warehouse).
        /// Matches by (LocationType, LocationId) only, with optional Ts cutoff.
        /// </summary>
        public Task<decimal> GetOnHandAtLocationAsync(
            int itemId, int outletId, InventoryLocationType locType, int locId, DateTime? atUtc = null, CancellationToken ct = default)
        {
            // NOTE: 'outletId' is not used for filtering on purpose; kept for API compatibility.
            var q = _db.StockEntries.AsNoTracking()
                .Where(e => e.ItemId == itemId
                         && e.LocationType == locType
                         && e.LocationId == locId);

            if (atUtc.HasValue) q = q.Where(e => e.Ts <= atUtc.Value);
            return q.SumAsync(e => e.QtyChange, ct);
        }

        /// <summary>
        /// Ensures that applying all given deltas would not push any location negative.
        /// Grouping and balances are computed by (ItemId, LocationType, LocationId) ONLY.
        /// 'outletId' is carried for message context, not for matching.
        /// </summary>
        public async Task EnsureNoNegativeAtLocationAsync(
            (int itemId, int outletId, InventoryLocationType locType, int locId, decimal delta)[] deltas,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            if (deltas == null || deltas.Length == 0) return;

            // First group the proposed changes (we still keep outletId for the error message)
            var grouped = deltas
                .GroupBy(d => new { d.itemId, d.outletId, d.locType, d.locId })
                .Select(g => new
                {
                    g.Key.itemId,
                    g.Key.outletId,
                    g.Key.locType,
                    g.Key.locId,
                    delta = g.Sum(x => x.delta)
                })
                .ToList();

            var itemIds = grouped.Select(x => x.itemId).Distinct().ToList();
            var locTypes = grouped.Select(x => x.locType).Distinct().ToList();
            var locIds = grouped.Select(x => x.locId).Distinct().ToList();

            // BALANCES: match by (ItemId, LocationType, LocationId) ONLY — no OutletId filter here
            var balancesQ = _db.StockEntries.AsNoTracking()
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
                    .Where(b => b.ItemId == g.itemId
                             && b.LocationType == g.locType
                             && b.LocationId == g.locId)
                    .Select(b => b.OnHand)
                    .DefaultIfEmpty(0m)
                    .Single();

                if (onHand + g.delta < 0m)
                {
                    // Keep outletId in the message for clarity, but matching was by (locType, locId)
                    throw new InvalidOperationException(
                        $"Negative stock for Item#{g.itemId} at Outlet#{g.outletId} {g.locType}#{g.locId} " +
                        $"(on-hand {onHand:0.####}, delta {g.delta:0.####}).");
                }
            }
        }
    }
}
