using System;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IInventoryReadService
    {
        // Sum of StockEntry.QtyChange for an item at a location strictly before cutoffUtc
        Task<decimal> GetOnHandAsync(int itemId, InventoryLocationType locType, int locId, DateTime cutoffUtc, CancellationToken ct = default);
    }
}
