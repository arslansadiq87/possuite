// Pos.Domain.Services/IInvoiceSettingsScopedService.cs
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Settings;

namespace Pos.Domain.Services;

public interface IInvoiceSettingsScopedService
{
    Task<InvoiceSettingsScoped> GetGlobalAsync(CancellationToken ct = default);
    Task<InvoiceSettingsScoped> GetForOutletAsync(int outletId, CancellationToken ct = default);
    Task UpsertAsync(InvoiceSettingsScoped model, CancellationToken ct = default);
}
