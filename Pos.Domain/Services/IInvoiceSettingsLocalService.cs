using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Settings;

namespace Pos.Domain.Services;

public interface IInvoiceSettingsLocalService
{
    Task<InvoiceSettingsLocal> GetForCounterAsync(int counterId, CancellationToken ct = default);
    Task<InvoiceSettingsLocal> UpsertAsync(InvoiceSettingsLocal model, CancellationToken ct = default);
    Task<InvoiceSettingsLocal> GetForCounterWithFallbackAsync(int? counterId, CancellationToken ct = default);

}
