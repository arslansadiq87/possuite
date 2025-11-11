using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface ILookupService
    {
        Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken ct = default);
        Task<IReadOnlyList<Outlet>> GetOutletsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<int>> GetUserOutletIdsAsync(int userId, CancellationToken ct = default);
        Task<IReadOnlyList<Account>> GetAccountsAsync(int? outletId, CancellationToken ct = default);

    }
}
