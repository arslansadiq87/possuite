using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models;

namespace Pos.Domain.Services
{
    public interface IOutletCounterService
    {
        // Queries
        Task<List<OutletRow>> GetOutletsAsync(CancellationToken ct = default);
        Task<List<CounterRow>> GetCountersAsync(int outletId, CancellationToken ct = default);

        // Outlet commands
        Task<int> AddOrUpdateOutletAsync(Outlet outlet, string? user = null, CancellationToken ct = default);
        Task DeleteOutletAsync(int outletId, CancellationToken ct = default);

        // Counter commands
        Task<int> AddOrUpdateCounterAsync(Counter counter, string? user = null, CancellationToken ct = default);
        Task DeleteCounterAsync(int counterId, CancellationToken ct = default);

        // Binding
        Task AssignThisPcAsync(int outletId, int counterId, string machine, CancellationToken ct = default);
        Task UnassignThisPcAsync(string machine, CancellationToken ct = default);

        // Lookups
        Task<Outlet?> GetOutletAsync(int outletId, CancellationToken ct = default);
        Task<Counter?> GetCounterAsync(int counterId, CancellationToken ct = default);

        // Uniqueness checks
        Task<bool> IsOutletCodeTakenAsync(string code, int? excludingId = null, CancellationToken ct = default);
        Task<bool> IsCounterNameTakenAsync(int outletId, string name, int? excludingId = null, CancellationToken ct = default);

        // Convenience upserts (after dialog)
        Task UpsertOutletByIdAsync(int outletId, CancellationToken ct = default);
        Task UpsertCounterByIdAsync(int counterId, CancellationToken ct = default);

        // USER–OUTLET ASSIGNMENTS
        Task<List<UserOutlet>> GetUserOutletsAsync(int userId, CancellationToken ct = default);
        Task<UserOutlet?> GetUserOutletAsync(int userId, int outletId, CancellationToken ct = default);
        Task AssignOutletAsync(int userId, int outletId, UserRole role, CancellationToken ct = default);
        Task UpdateUserOutletRoleAsync(int userId, int outletId, UserRole newRole, CancellationToken ct = default);
        Task RemoveUserOutletAsync(int userId, int outletId, CancellationToken ct = default);
    }
}
