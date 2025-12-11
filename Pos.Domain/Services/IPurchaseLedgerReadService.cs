using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Reports;

namespace Pos.Domain.Services
{
    public interface IPurchaseLedgerReadService
    {
        // CHANGE signature to accept nullable dates
        Task<IReadOnlyList<PurchaserLedgerRow>> GetSupplierLedgerAsync(
            DateTime? fromUtc, DateTime? toUtcExclusive, int? supplierId, int? outletId,
            CancellationToken ct = default);

    }
}
