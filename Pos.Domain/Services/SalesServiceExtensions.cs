using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Sales;

namespace Pos.Domain.Services
{
    public static class SalesServiceExtensions
    {
        /// <summary>Alias for GetSaleForEditAsync to standardize naming in UI.</summary>
        public static Task<EditSaleLoadDto> LoadForEditAsync(
            this ISalesService svc,
            int saleId,
            CancellationToken ct = default)
            => svc.GetSaleForEditAsync(saleId);

        /// <summary>Alias for SaveAmendmentAsync to standardize naming in UI.</summary>
        public static Task<EditSaleSaveResult> SaveEditAsync(
            this ISalesService svc,
            EditSaleSaveRequest req,
            CancellationToken ct = default)
            => svc.SaveAmendmentAsync(req, ct);
    }
}
