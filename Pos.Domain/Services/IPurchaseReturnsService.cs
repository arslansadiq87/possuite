// Pos.Domain/Services/IPurchaseReturnsService.cs
#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Purchases;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Purchase Return workflow:
    ///  - Return WITHOUT invoice  (free-form, constrained by on-hand)
    ///  - Return WITH invoice     (constrained by purchased qty + on-hand)
    ///  - Amend return
    ///  - Void return
    /// </summary>
    public interface IPurchaseReturnsService
    {
        // ----------------------------------------------------
        //  NEW RETURN: WITH INVOICE
        // ----------------------------------------------------
        /// <summary>
        /// Build a draft return against a FINAL purchase.
        /// Pre-fills remaining-allowed quantity per line
        /// (delta = purchased - already returned), plus max caps.
        /// </summary>
        Task<PurchaseReturnDraft> BuildReturnDraftFromInvoiceAsync(
            int originalPurchaseId,
            CancellationToken ct = default);

        /// <summary>
        /// Finalize and post a Purchase Return that references a FINAL purchase.
        /// - Enforces max allowed per-line based on original + previous returns.
        /// - Also enforces on-hand caps on the selected source (outlet/warehouse).
        /// - Posts stock OUT of the source.
        /// - Posts refund to supplier: AP reduced + cash/bank refund (if any).
        /// </summary>
        Task<Purchase> FinalizeReturnFromInvoiceAsync(
            Purchase header,
            IEnumerable<PurchaseReturnDraftLine> draftLines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
            string user,
            CancellationToken ct = default);

        // ----------------------------------------------------
        //  NEW RETURN: WITHOUT INVOICE
        // ----------------------------------------------------
        /// <summary>
        /// Finalize and post a free-form Purchase Return (no RefPurchaseId).
        /// - Source is header.LocationType + OutletId/WarehouseId.
        /// - Per-line max is limited by on-hand at that source (no negatives).
        /// - Posts stock OUT of the source.
        /// - Posts refund to supplier if refunds collection is non-empty.
        /// </summary>
        Task<Purchase> FinalizeReturnWithoutInvoiceAsync(
            Purchase header,
            IEnumerable<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
            string user,
            CancellationToken ct = default);

        // ----------------------------------------------------
        //  AMEND RETURN
        // ----------------------------------------------------
        /// <summary>
        /// Load an existing posted Purchase Return (header + lines) for amendment.
        /// </summary>
        Task<(Purchase Header, List<PurchaseLine> Lines)> LoadReturnForAmendAsync(
            int returnId,
            CancellationToken ct = default);

        /// <summary>
        /// Save an amendment to an existing Purchase Return.
        /// - Adjusts stock delta (no negatives on source).
        /// - Adjusts GL for supplier and refund accounts (delta-based, revision).
        /// </summary>
        Task<Purchase> FinalizeReturnAmendAsync(
            Purchase header,
            IEnumerable<PurchaseLine> newLines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
            string user,
            CancellationToken ct = default);

        // ----------------------------------------------------
        //  VOID RETURN
        // ----------------------------------------------------
        /// <summary>
        /// Void a posted Purchase Return.
        /// - Reverses stock (puts it BACK into the source).
        /// - Reverses GL rows for refund + AP reduction.
        /// </summary>
        Task VoidReturnAsync(
            int returnId,
            string reason,
            string? user = null,
            CancellationToken ct = default);

    }
}
