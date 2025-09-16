// Pos.Domain/Services/ISalesService.cs
using System.Threading.Tasks;

namespace Pos.Domain.Services
{
    public interface ISalesService
    {
        /// <summary>
        /// Reverse original Final sale (stock + cash), mark it as Revised,
        /// create a new Draft sale (same invoice number, Revision+1), and link both ends.
        /// Returns new Draft sale Id.
        /// </summary>
        Task<int> AmendByReversalAndReissueAsync(
            int originalSaleId,
            int? tillSessionId,
            int userId,
            string? reason = null);
    }
}
