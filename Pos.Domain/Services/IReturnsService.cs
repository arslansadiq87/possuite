// Pos.Domain/Services/IReturnsService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Pos.Domain.Models.Sales;

namespace Pos.Domain.Services
{
    public record ReturnNoInvLine(int ItemId, decimal Qty, decimal UnitPrice, decimal UnitCost, decimal Discount = 0m);

    public interface IReturnsService
    {
        /// <summary>
        /// Creates and posts a Return (IsReturn=true) without OriginalSaleId.
        /// Adds stock and records cash-out. Returns the new Sale (return) Id.
        /// </summary>
        Task<int> CreateReturnWithoutInvoiceAsync(
            int outletId,
            int counterId,
            int? tillSessionId,
            int userId,
            IEnumerable<ReturnNoInvLine> lines,
            int? customerId = null,
            string? customerName = null,
            string? customerPhone = null,
            string? reason = null);

        Task<EditReturnLoadDto> LoadReturnForAmendAsync(int returnSaleId, CancellationToken ct = default);

        Task<EditReturnFinalizeResult> FinalizeReturnAmendAsync(EditReturnFinalizeRequest request, CancellationToken ct = default);
    }
}
