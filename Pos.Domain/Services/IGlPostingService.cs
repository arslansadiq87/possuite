// Pos.Domain/Services/IGlPostingService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Hr;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Posts GL for business documents. No EF types allowed here.
    /// Every method accepts CancellationToken.
    /// </summary>
    public interface IGlPostingService
    {
        // Sales
        Task PostSaleAsync(Sale sale, CancellationToken ct = default);
        Task PostSaleReturnAsync(Sale sale, CancellationToken ct = default);
        Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax, CancellationToken ct = default);
        Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default);

        // Purchases
        Task PostPurchaseAsync(Purchase p, CancellationToken ct = default);
        Task PostPurchaseReturnAsync(Purchase p, CancellationToken ct = default);

        // Vouchers
        Task PostVoucherAsync(Voucher v, CancellationToken ct = default);
        Task PostVoucherVoidAsync(Voucher voucherToVoid, CancellationToken ct = default);
        Task PostVoucherRevisionAsync(Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default);

        // Payroll
        Task PostPayrollAccrualAsync(PayrollRun run, CancellationToken ct = default);
        Task PostPayrollPaymentAsync(PayrollRun run, CancellationToken ct = default);

        // Till close
        Task PostTillCloseAsync(TillSession session, decimal declaredCash, decimal systemCash, CancellationToken ct = default);
    }
}
