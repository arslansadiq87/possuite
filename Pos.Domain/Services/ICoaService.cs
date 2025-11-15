// Pos.Domain/Services/ICoaService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Accounting;

namespace Pos.Domain.Services
{
    public interface ICoaService
    {
        // Load
        Task<List<CoaAccount>> GetAllAsync(CancellationToken ct = default);

        // Create
        Task<(int id, string code)> CreateHeaderAsync(int parentId, string name, CancellationToken ct = default);
        Task<(int id, string code)> CreateAccountAsync(int parentId, string name, CancellationToken ct = default);

        // Edit / Delete
        Task EditAsync(AccountEdit edit, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);

        // Openings
        Task SaveOpeningsAsync(IEnumerable<OpeningChange> changes, CancellationToken ct = default);
        Task LockAllOpeningsAsync(CancellationToken ct = default);

        // Helpers used elsewhere
        Task<int> EnsureOutletCashAccountAsync(int outletId, CancellationToken ct = default);
        Task<int> GetCashAccountIdAsync(int? outletId, CancellationToken ct = default);
        Task<int> EnsureOutletTillAccountAsync(int outletId, CancellationToken ct = default);
        Task<int> GetTillAccountIdAsync(int outletId, CancellationToken ct = default);

        // Convenience creators used by the VM toolbar
        Task AddCashForOutletAsync(CancellationToken ct = default);
        Task AddStaffAccountAsync(CancellationToken ct = default);

        Task<int> EnsureInventoryAccountIdAsync(CancellationToken ct = default);
        Task<int> EnsureSupplierAccountIdAsync(int partyId, CancellationToken ct = default);

       
    }
}
