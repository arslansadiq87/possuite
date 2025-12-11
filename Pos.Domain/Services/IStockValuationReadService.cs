using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Reports;

namespace Pos.Domain.Services
{
    public interface IStockValuationReadService
    {
        Task<IReadOnlyList<StockValuationRow>> GetCostViewAsync(
            int outletId,
            DateTime? cutoffUtc,
            CancellationToken ct = default);

        Task<IReadOnlyList<StockValuationRow>> GetSaleViewAsync(
            int outletId,
            DateTime? cutoffUtc,
            CancellationToken ct = default);
    }
}
