using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models;

namespace Pos.Domain.Services
{
    public interface IOtherAccountService
    {
        Task<List<OtherAccount>> GetAllAsync(CancellationToken ct = default);
        Task<OtherAccount?> GetAsync(int id, CancellationToken ct = default);
        Task<string> GenerateNextOtherCodeAsync(CancellationToken ct = default);
        Task UpsertAsync(OtherAccountUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
