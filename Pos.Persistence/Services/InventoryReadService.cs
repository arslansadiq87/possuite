using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class InventoryReadService : IInventoryReadService
    {
        private readonly PosClientDbContext _db;
        public InventoryReadService(PosClientDbContext db) => _db = db;

        public async Task<decimal> GetOnHandAsync(int itemId, InventoryLocationType locType, int locId, DateTime cutoffUtc, CancellationToken ct = default)
        {
            if (locId <= 0) return 0m;

            var qty = await _db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.ItemId == itemId
                         && e.LocationType == locType
                         && e.LocationId == locId
                         && e.Ts < cutoffUtc)
                .SumAsync(e => (decimal?)e.QtyChange, ct) ?? 0m;

            return Math.Max(qty, 0m);
        }
    }
}
