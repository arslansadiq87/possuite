// Pos.Domain/Services/ILedgerQueryService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Accounting;

namespace Pos.Domain.Services
{
    public interface ILedgerQueryService
    {
        Task<(decimal opening, List<LedgerRow> rows, decimal closing)>
            GetAccountLedgerAsync(int accountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

        Task<int> GetOutletCashAccountIdAsync(int outletId, CancellationToken ct = default);

        Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CancellationToken ct = default);

        Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CashBookScope scope, CancellationToken ct = default);
    }
}
