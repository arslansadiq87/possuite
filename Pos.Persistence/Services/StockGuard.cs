using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Inventory;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    /// <inheritdoc/>
    public sealed class StockGuard : IStockGuard
    {
        private readonly IInventoryReadService _read;

        public StockGuard(IInventoryReadService read)
        {
            _read = read;
        }

        /// <summary>
        /// Legacy convenience: outlet-only on-hand (now delegated).
        /// </summary>
        public Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default)
        {
            // Use centralized read path; cutoff = now
            return _read.GetOnHandAtLocationAsync(itemId, InventoryLocationType.Outlet, outletId, DateTime.UtcNow, ct);
        }

        public Task<decimal> GetOnHandAtLocationAsync(
            int itemId,
            int outletId,                          // kept only for message context
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            // Centralized: defer to read service (uses strict-before + clamp)
            return _read.GetOnHandAtLocationAsync(itemId, locType, locId, atUtc, ct);
        }

        public async Task EnsureNoNegativeAtLocationAsync(
            IEnumerable<StockDeltaDto> deltas,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            if (deltas is null) return;

            var cutoff = atUtc ?? DateTime.UtcNow;

            // Group per (item,loc), retain OutletId purely for clearer error messages
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

            foreach (var g in grouped)
            {
                var onHand = await _read
                    .GetOnHandAsync(g.ItemId, g.LocType, g.LocId, cutoff, ct)
                    .ConfigureAwait(false);

                if (onHand + g.Delta < 0m)
                {
                    // OutletId is contextual only; actual match is (LocType, LocId)
                    throw new InvalidOperationException(
                        $"Negative stock for Item#{g.ItemId} at Outlet#{g.OutletId} {g.LocType}#{g.LocId} " +
                        $"(on-hand {onHand:0.####}, delta {g.Delta:0.####}).");
                }
            }
        }
    }
}
