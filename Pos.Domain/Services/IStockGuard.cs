// Pos.Domain/Services/IStockGuard.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Inventory;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Central negative-stock guard. Reusable by all services/UIs.
    /// On-hand is computed strictly by (LocationType, LocationId) to
    /// match StockReport and avoid discrepancies with legacy OutletId.
    /// </summary>
    public interface IStockGuard
    {
        /// <summary>
        /// Convenience: on-hand at an OUTLET location (by outletId).
        /// Uses (LocationType=Outlet, LocationId=outletId); does NOT filter by OutletId column.
        /// </summary>
        Task<decimal> GetOnHandAsync(int itemId, int outletId, CancellationToken ct = default);

        /// <summary>
        /// On-hand at an arbitrary location (Outlet or Warehouse),
        /// matched by (LocationType, LocationId) only, with optional timestamp cutoff.
        /// </summary>
        Task<decimal> GetOnHandAtLocationAsync(
            int itemId,
            int outletId,                   // kept for message context/back-compat (not used to filter)
            InventoryLocationType locType,
            int locId,
            DateTime? atUtc = null,
            CancellationToken ct = default);

        /// <summary>
        /// Ensures that applying all given deltas would not push any location negative.
        /// Grouping/balances are computed by (ItemId, LocationType, LocationId) ONLY.
        /// 'OutletId' is carried for error message context.
        /// </summary>
        Task EnsureNoNegativeAtLocationAsync(
            IEnumerable<StockDeltaDto> deltas,
            DateTime? atUtc = null,
            CancellationToken ct = default);
    }
}
