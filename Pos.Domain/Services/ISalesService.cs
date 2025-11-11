using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Sales;

namespace Pos.Domain.Services
{
    public interface ISalesService
    {
        // Lookups / reads
        Task<IReadOnlyList<ItemIndexDto>> GetItemIndexAsync(CancellationToken ct = default);
        Task<IReadOnlyList<StaffLiteDto>> GetSalesmenAsync(CancellationToken ct = default);
        Task<InvoicePreviewDto> GetInvoicePreviewAsync(int counterId, CancellationToken ct = default);
        Task<TillSession?> GetOpenTillAsync(int outletId, int counterId, CancellationToken ct = default);
        Task<SaleResumeDto?> LoadHeldAsync(int saleId, CancellationToken ct = default);
        // Return-from-invoice read model
        Task<ReturnFromInvoiceLoadDto> GetReturnFromInvoiceAsync(int saleId, CancellationToken ct = default);
        // Guards
        Task<bool> GuardSaleQtyAsync(int outletId, int itemId, decimal proposedQty, CancellationToken ct = default);

        // Commands
        Task<int> HoldAsync(SaleHoldRequest req, CancellationToken ct = default);
        Task<Sale> FinalizeAsync(SaleFinalizeRequest req, CancellationToken ct = default);

        // Existing (amend flow)
        Task<int> AmendByReversalAndReissueAsync(int originalSaleId, int? tillSessionId, int userId, string? reason = null);
        
        Task<EditSaleLoadDto> GetSaleForEditAsync(int saleId);
        /// Guard extra OUT beyond original qty for this item.
        Task<bool> GuardEditExtraOutAsync(
          int outletId,
          int itemId,
          decimal originalQty,
          decimal proposedCartQty,
          CancellationToken ct = default);
        Task<EditSaleSaveResult> SaveAmendmentAsync(EditSaleSaveRequest req, CancellationToken ct = default);
    }
}
