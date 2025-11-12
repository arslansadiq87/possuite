// Pos.Domain/Services/IArApQueryService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Pos.Domain.DTO;
using Pos.Domain.Entities;   // RoleType

namespace Pos.Domain.Services
{
    public interface IArApQueryService
    {
        Task<List<ArApRow>> GetAccountsReceivableAsync(int? outletId = null, bool includeZero = false);
        Task<List<ArApRow>> GetAccountsPayableAsync(int? outletId = null, bool includeZero = false);
        Task<decimal> GetTotalAsync(RoleType role, int? outletId = null);
    }
}
