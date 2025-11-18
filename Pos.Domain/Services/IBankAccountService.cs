using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models;

namespace Pos.Domain.Services
{
    public interface IBankAccountService
    {
        Task<BankAccountViewDto?> GetByAccountIdAsync(int accountId, CancellationToken ct = default);
        Task<BankAccountViewDto?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<BankAccountViewDto>> SearchAsync(string? q = null, CancellationToken ct = default);

        // Creates GL account under 113 and a BankAccount row atomically
        Task<int> CreateAsync(int bankHeaderAccountId, BankAccountUpsertDto dto, CancellationToken ct = default);

        // Updates both the BankAccount row and the linked GL account Name
        Task UpdateAsync(BankAccountUpsertDto dto, CancellationToken ct = default);
    }
}
