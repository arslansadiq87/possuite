using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Reports;

namespace Pos.Domain.Services
{
    /// <summary>Read-only reports aggregation surface.</summary>
    public interface IReportsService
    {
        /// <summary>
        /// Item-level stock on hand (SKU/variant rows).
        /// If <paramref name="scopeId"/> is null, aggregates all locations of the specified <paramref name="scopeType"/>.
        /// </summary>
        Task<List<StockOnHandItemRow>> StockOnHandByItemAsync(
            InventoryLocationType scopeType,
            int? scopeId,
            CancellationToken ct = default);

        /// <summary>
        /// Product-level stock on hand (grouped).
        /// If <paramref name="scopeId"/> is null, aggregates all locations of the specified <paramref name="scopeType"/>.
        /// </summary>
        Task<List<StockOnHandProductRow>> StockOnHandByProductAsync(
            InventoryLocationType scopeType,
            int? scopeId,
            CancellationToken ct = default);

        /// <summary>Returns outlets the user can see. Admins get all outlets.</summary>
        Task<List<Outlet>> GetOutletsForUserAsync(int userId, bool isAdmin, CancellationToken ct = default);

        /// <summary>Returns all warehouses (admins only surface them in UI).</summary>
        Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default);
    }
}
