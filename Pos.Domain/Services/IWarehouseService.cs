using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IWarehouseService
    {
        Task<List<Warehouse>> SearchAsync(string? term, bool showInactive, int take = 1000, CancellationToken ct = default);
        Task SetActiveAsync(int warehouseId, bool active, CancellationToken ct = default);
        Task<Warehouse> SaveWarehouseAsync(Warehouse input, CancellationToken ct = default);
        Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);
        Task<string> SuggestNextCodeAsync(CancellationToken ct = default);

    }
}
