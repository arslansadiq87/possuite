using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IInvoiceSettingsService
    {
        // Full read (setting + resolved localization by lang, with outlet→global fallback)
        Task<(InvoiceSettings Settings, InvoiceLocalization Local)> GetAsync(
            int? outletId, string? lang, CancellationToken ct = default);

        // Convenience getters
        Task<string?> GetPrinterAsync(int? outletId, CancellationToken ct = default);
        Task<int> GetPaperWidthAsync(int? outletId, CancellationToken ct = default);
        Task<int?> GetSalesCardClearingAccountIdAsync(int? outletId, CancellationToken ct = default);
        Task<int?> GetPurchaseBankAccountIdAsync(int? outletId, CancellationToken ct = default);

        // Save settings + localizations atomically
        Task SaveAsync(
            InvoiceSettings settings,
            IEnumerable<InvoiceLocalization> localizations,
            CancellationToken ct = default);
    }
}
