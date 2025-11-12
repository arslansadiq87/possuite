using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Hr;

namespace Pos.Domain.Services.Hr
{
    /// <summary>
    /// Read-only access to staff directory data.
    /// </summary>
    public interface IStaffReadService
    {
        /// <summary>All active staff who act as salesmen, ordered by FullName.</summary>
        Task<List<Staff>> GetSalesmenAsync(CancellationToken ct = default);

        /// <summary>All active staff, ordered by FullName.</summary>
        Task<List<Staff>> GetAllActiveAsync(CancellationToken ct = default);
    }
}
