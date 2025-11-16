#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Hr;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Public (DB-agnostic) GL posting interface for application/services to call.
    /// Purchase-first scope; sales/vouchers/payroll kept for later fill-in.
    /// </summary>
    public interface IGlPostingService
    {
        // ----- Purchases -----
        Task PostPurchaseAsync(Purchase p, CancellationToken ct = default);
        Task PostPurchaseRevisionAsync(Purchase amended, decimal deltaGrand, CancellationToken ct = default);
        Task PostPurchaseReturnAsync(Purchase p, CancellationToken ct = default);

        Task PostPurchaseVoidAsync(Purchase p, CancellationToken ct = default);
        Task PostPurchaseReturnVoidAsync(Purchase p, CancellationToken ct = default);

        // ----- Sales (stubs; to be implemented after purchases) -----
        Task PostSaleAsync(Sale sale, CancellationToken ct = default);
        Task PostSaleRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default);
        Task PostSaleReturnAsync(Sale sale, CancellationToken ct = default);
        Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default);

        // ----- Vouchers (stubs) -----
        Task PostVoucherAsync(Voucher v, CancellationToken ct = default);
        Task PostVoucherVoidAsync(Voucher voucherToVoid, CancellationToken ct = default);
        Task PostVoucherRevisionAsync(Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default);

        // ----- Payroll (stubs) -----
        Task PostPayrollAccrualAsync(PayrollRun run, CancellationToken ct = default);
        Task PostPayrollPaymentAsync(PayrollRun run, CancellationToken ct = default);

        /// <summary>
        /// Posts GL for a till close: move declared cash from Till (counter) to Cash-in-Hand,
        /// and record cash over/short so Till ends at opening float.
        /// </summary>
        Task PostTillCloseAsync(TillSession session, decimal declaredToMove, decimal systemCash, CancellationToken ct = default);
        Task PostPurchasePaymentAddedAsync(Purchase p, PurchasePayment pay, CancellationToken ct);
        Task PostPurchasePaymentReversalAsync(Purchase p, PurchasePayment pay, CancellationToken ct);
        // Pos.Domain/Services/IGlPostingService.cs
        Task PostPurchasePaymentAsync(
            Purchase p,
            int partyId,
            int counterAccountId,
            decimal amount,
            TenderMethod method,
            CancellationToken ct = default);

        /// <summary>
        /// Post or adjust GL for an Opening Stock document.
        /// This should be called when locking, and again on re-lock after edits (delta-based).
        /// </summary>
        Task PostOpeningStockAsync(
          StockDoc doc,
          IEnumerable<StockEntry> openingEntries,
          int offsetAccountId,
          CancellationToken ct = default);

        Task UnlockOpeningStockAsync(
            StockDoc doc,
            CancellationToken ct = default);
    }
}
