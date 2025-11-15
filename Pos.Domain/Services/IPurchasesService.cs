// Pos.Domain/Services/IPurchasesService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Purchases;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Purchase workflow service (draft, post, amend, payments, void).
    /// NOTE: Purchase Returns will live in a separate IPurchaseReturnsService.
    /// </summary>
    public interface IPurchasesService
    {
        /// <summary>
        /// DRAFT SAVE:
        /// - Persists header + lines
        /// - Posts payments ONLY (advance). No stock. No gross AP.
        /// </summary>
        Task<Purchase> SaveDraftAsync(
            Purchase draft,
            IEnumerable<PurchaseLine> lines,
            string? user = null,
            CancellationToken ct = default);

        /// <summary>
        /// POST & SAVE (finalize receive):
        /// - Posts stock (with negative-stock guard), gross AP, and payment deltas (cash/bank).
        /// - onReceivePayments are optional additional payments captured at receive time.
        /// </summary>
        Task<Purchase> FinalizeReceiveAsync(
            Purchase purchase,
            IEnumerable<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> onReceivePayments,
            int outletId,
            int supplierId,
            int? tillSessionId,   // ignored for purchases (no tills) — kept for UI signature compatibility
            int? counterId,       // ignored for purchases
            string user,
            CancellationToken ct = default);

        /// <summary>
        /// Thin alias to FinalizeReceiveAsync for existing UI call sites that pass fewer args.
        /// </summary>
        Task<Purchase> ReceiveAsync(
            Purchase model,
            IEnumerable<PurchaseLine> lines,
            string? user = null,
            CancellationToken ct = default);

        // ---------------- Payments (Cash or Bank only) ----------------

        /// <summary>Add a payment line and immediately post payment delta to GL.</summary>
        Task<PurchasePayment> AddPaymentAsync(
            int purchaseId,
            PurchasePaymentKind kind,
            TenderMethod method,
            decimal amount,
            string? note,
            int outletId,
            int supplierId,
            string user,
            int? bankAccountId = null,
            CancellationToken ct = default);

        /// <summary>Update an existing payment amount/note and re-post payment delta.</summary>
        // ADD this overload (keep your existing one too)
        Task UpdatePaymentAsync(
            int paymentId,
            decimal newAmount,
            TenderMethod newMethod,
            string? newNote,
            string user,
            CancellationToken ct = default);


        /// <summary>Mark a payment ineffective (remove) and re-post payment delta.</summary>
        Task RemovePaymentAsync(
            int paymentId,
            string user,
            CancellationToken ct = default);

        /// <summary>List effective payments for a purchase.</summary>
        Task<IReadOnlyList<PurchasePayment>> GetPaymentsAsync(
            int purchaseId,
            CancellationToken ct = default);

        // ---------------- Bank/Cash helpers ----------------

        /// <summary>Returns true if a default Purchase Bank account is configured for the outlet.</summary>
        Task<bool> IsPurchaseBankConfiguredAsync(
            int outletId,
            CancellationToken ct = default);

        /// <summary>Lists company-scope Bank accounts selectable for purchases.</summary>
        Task<List<Account>> ListBankAccountsForOutletAsync(
            int outletId,
            CancellationToken ct = default);

        /// <summary>Gets the configured default Purchase Bank account id for the outlet (if any).</summary>
        Task<int?> GetConfiguredPurchaseBankAccountIdAsync(
            int outletId,
            CancellationToken ct = default);

        // ---------------- Void ----------------

        /// <summary>
        /// Void a finalized purchase:
        /// - Guards against negative stock at destination,
        /// - Writes reversing stock entries,
        /// - Inactivates all GL rows in the purchase chain (gross + payments).
        /// </summary>
        Task VoidPurchaseAsync(
            int purchaseId,
            string reason,
            string? user = null,
            CancellationToken ct = default);

        Task<(decimal unitCost, decimal discount, decimal taxRate)?> GetLastPurchaseDefaultsAsync(int itemId, CancellationToken ct = default);
        Task<Purchase?> LoadDraftWithLinesAsync(int id, CancellationToken ct = default);

        Task<Purchase> LoadWithLinesAsync(int id, CancellationToken ct = default);
        Task<List<PurchaseLineEffective>> GetEffectiveLinesAsync(int purchaseId, CancellationToken ct = default);
    }
}
