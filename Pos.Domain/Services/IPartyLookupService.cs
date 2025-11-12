using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Party lookup abstraction (customer/supplier/general) with outlet visibility.
    /// NOTE: All methods are async and accept CancellationToken (house rule).
    /// </summary>
    public interface IPartyLookupService
    {
        /// <summary>Active suppliers visible to the outlet; LIKE on Name/Phone/Email/Tax.</summary>
        Task<List<Party>> SearchSuppliersAsync(string term, int outletId, int take = 30, CancellationToken ct = default);

        /// <summary>Supplier by exact (case-insensitive) name if visible to the outlet; otherwise null.</summary>
        Task<Party?> FindSupplierByExactNameAsync(string name, int outletId, CancellationToken ct = default);

        /// <summary>
        /// Generic party search with optional role filter (null = both).
        /// Respects outlet visibility; LIKE on Name/Phone/Email/Tax.
        /// </summary>
        Task<List<Party>> SearchPartiesAsync(string term, RoleType? roleFilter, int outletId, int take = 30, CancellationToken ct = default);

        /// <summary>Active customers visible to the outlet; LIKE on Name/Phone/Email/Tax.</summary>
        Task<List<Party>> SearchCustomersAsync(string term, int outletId, int take = 30, CancellationToken ct = default);

        /// <summary>Customer by exact (case-insensitive) name if visible to the outlet; otherwise null.</summary>
        Task<Party?> FindCustomerByExactNameAsync(string name, int outletId, CancellationToken ct = default);
    }
}
