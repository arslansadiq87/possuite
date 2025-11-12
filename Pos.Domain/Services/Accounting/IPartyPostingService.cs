using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;

namespace Pos.Domain.Services.Accounting
{
    /// <summary>
    /// Single source of truth for party ledger postings and balance snapshots.
    /// All party-related financial mutations must go through this service.
    /// </summary>
    public interface IPartyPostingService
    {
        /// <summary>
        /// Posts a party ledger entry (AR/AP) and updates the PartyBalance snapshot atomically.
        /// Use <paramref name="scope"/> to decide whether the posting is per-company or per-outlet.
        /// For Outlet scope, <paramref name="outletId"/> must be provided.
        /// </summary>
        Task PostAsync(
            int partyId,
            BillingScope scope,
            int? outletId,
            PartyLedgerDocType docType,
            int docId,
            decimal debit,
            decimal credit,
            string? memo = null,
            CancellationToken ct = default);
    }
}
