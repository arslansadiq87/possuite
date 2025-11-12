// Pos.Domain/Services/IPartyService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Parties;

namespace Pos.Domain.Services
{
    public interface IPartyService
    {
        Task<List<PartyRowDto>> SearchAsync(
            string? term,
            bool onlyActive,
            bool includeCustomer,
            bool includeSupplier,
            CancellationToken ct = default);

        Task<Party?> GetPartyAsync(int id, CancellationToken ct = default);

        Task SavePartyAsync(
            int? id,
            string name,
            string? phone,
            string? email,
            string? taxNumber,
            bool isActive,
            bool isShared,
            bool roleCustomer,
            bool roleSupplier,
            IEnumerable<(int OutletId, bool IsActive, bool AllowCredit, decimal? CreditLimit)> outlets,
            CancellationToken ct = default);

        Task<string?> GetPartyNameAsync(int partyId, CancellationToken ct = default);
    }
}
