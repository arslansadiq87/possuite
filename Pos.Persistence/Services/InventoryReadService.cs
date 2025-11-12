using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    /// <summary>Single source of truth for inventory balances (on-hand).</summary>
    public sealed class InventoryReadService : IInventoryReadService
    {
        private readonly PosClientDbContext _db;
        public InventoryReadService(PosClientDbContext db) => _db = db;

        public async Task<decimal> GetOnHandAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default)
        {
            if (locId <= 0 || itemId <= 0) return 0m;

            var qty = await _db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.ItemId == itemId
                         && e.LocationType == locType
                         && e.LocationId == locId
                         && e.Ts < cutoffUtc)           // strict-before
                .SumAsync(e => (decimal?)e.QtyChange, ct)
                .ConfigureAwait(false) ?? 0m;

            return Math.Max(qty, 0m);                   // clamp to non-negative
        }

        public Task<decimal> GetOnHandAtLocationAsync(
            int itemId,
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            var cutoff = atUtc ?? DateTime.UtcNow;
            return GetOnHandAsync(itemId, locType, locId, cutoff, ct);
        }

        public async Task<Dictionary<int, decimal>> GetOnHandBulkAsync(
            IEnumerable<int> itemIds,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default)
        {
            var ids = itemIds?.Where(x => x > 0).Distinct().ToArray() ?? Array.Empty<int>();
            if (locId <= 0 || ids.Length == 0) return ids.ToDictionary(i => i, _ => 0m);

            var rows = await _db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.LocationType == locType
                         && e.LocationId == locId
                         && e.Ts < cutoffUtc
                         && ids.Contains(e.ItemId))
                .GroupBy(e => e.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // clamp non-negative per item, include zeros for missing
            var map = rows.ToDictionary(x => x.ItemId, x => Math.Max(x.Qty, 0m));
            foreach (var id in ids)
                if (!map.ContainsKey(id)) map[id] = 0m;

            return map;
        }

        public Task<Dictionary<int, decimal>> GetOnHandBulkAsync(
            IEnumerable<int> itemIds,
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default)
        {
            var cutoff = atUtc ?? DateTime.UtcNow;
            return GetOnHandBulkAsync(itemIds, locType, locId, cutoff, ct);
        }

        // -------- NEW centralized “available for issue” helpers --------

        public async Task<decimal> GetAvailableForIssueAsync(
            int itemId, InventoryLocationType locType, int locId,
            DateTime cutoffUtc, decimal stagedUi = 0m, CancellationToken ct = default)
        {
            var onHand = await GetOnHandAsync(itemId, locType, locId, cutoffUtc, ct);
            var available = onHand - Math.Max(stagedUi, 0m); // staged (UI) reduces what can be issued
            return available > 0m ? available : 0m;
        }

        public async Task<Dictionary<int, decimal>> GetAvailableForIssueBulkAsync(
            IEnumerable<(int itemId, decimal stagedUi)> items,
            InventoryLocationType locType,
            int locId,
            DateTime cutoffUtc,
            CancellationToken ct = default)
        {
            var list = (items ?? Array.Empty<(int, decimal)>())
                .Where(t => t.itemId > 0)
                .ToList();
            var ids = list.Select(t => t.itemId).Distinct().ToArray();
            var onHand = await GetOnHandBulkAsync(ids, locType, locId, cutoffUtc, ct);

            var result = new Dictionary<int, decimal>(ids.Length);
            foreach (var (itemId, stagedUi) in list)
            {
                var baseOnHand = onHand.TryGetValue(itemId, out var oh) ? oh : 0m;
                var avail = baseOnHand - Math.Max(stagedUi, 0m);
                result[itemId] = avail > 0m ? avail : 0m;
            }
            // ensure every id appears at least once
            foreach (var id in ids) if (!result.ContainsKey(id)) result[id] = 0m;
            return result;
        }
    }
}
