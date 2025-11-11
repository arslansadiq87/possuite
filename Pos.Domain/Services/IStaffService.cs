using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Hr;   // Staff

namespace Pos.Domain.Services
{
    /// <summary>
    /// Staff service contract (EF-free). All methods are async and accept a CancellationToken.
    /// </summary>
    public interface IStaffService
    {
        // ─────────────── Queries ───────────────
        Task<List<Staff>> GetAllAsync(CancellationToken ct = default);
        Task<Staff?> GetAsync(int id, CancellationToken ct = default);
        Task<bool> IsNameTakenAsync(string name, int? excludingId = null, CancellationToken ct = default);
        Task<string> GenerateNextStaffCodeAsync(CancellationToken ct = default);

        // ─────────────── Commands ───────────────
        /// <summary>
        /// Create or update a Staff record, ensure it is linked to an Account under "63 Staff",
        /// enqueue upserts to the outbox, and return the saved Staff.Id.
        /// </summary>
        Task<int> CreateOrUpdateAsync(Staff input, CancellationToken ct = default);

        /// <summary>
        /// Delete Staff and enqueue a tombstone to outbox (idempotent).
        /// </summary>
        Task DeleteAsync(int staffId, CancellationToken ct = default);
    }
}
