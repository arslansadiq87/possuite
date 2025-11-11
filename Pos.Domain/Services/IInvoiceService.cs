using Pos.Domain.Models.Sales;

namespace Pos.Domain.Services
{
    public interface IInvoiceService
    {
        Task<IReadOnlyList<InvoiceSearchRowDto>> SearchLatestInvoicesAsync(
            int outletId, int counterId, DateTime? fromUtc, DateTime? toUtc, string? search, CancellationToken ct = default);

        Task<Dictionary<int, bool>> GetReturnHasBaseMapAsync(
            IEnumerable<int> returnSaleIds, CancellationToken ct = default);

        Task<(InvoiceDetailHeaderDto header, IReadOnlyList<InvoiceDetailLineDto> lines)>
            LoadSaleWithLinesAsync(int saleId, CancellationToken ct = default);

        Task<bool> AnyHeldAsync(int outletId, int counterId, CancellationToken ct = default);

        Task<bool> HasNonVoidedReturnAgainstAsync(int saleId, CancellationToken ct = default);

        Task VoidReturnAsync(int saleId, string reason, CancellationToken ct = default);

        Task VoidSaleAsync(int saleId, string reason, CancellationToken ct = default);
        
        // Held (draft) invoices
        Task<IReadOnlyList<HeldRowDto>> GetHeldAsync(int outletId, int counterId, CancellationToken ct = default);
        Task DeleteHeldAsync(int saleId, CancellationToken ct = default);
    }
}
