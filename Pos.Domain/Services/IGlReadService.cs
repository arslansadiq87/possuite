using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Domain.Services
{
    // Pos.Domain.Services
    public interface IGlReadService
    {
        Task<decimal> GetApBalanceForPurchaseAsync(
            Guid purchasePublicId,     // or string docNo
            int supplierAccountId,
            CancellationToken ct);
    }

}
