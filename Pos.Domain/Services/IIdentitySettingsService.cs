// Pos.Domain/Services/IIdentitySettingsService.cs
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IIdentitySettingsService
    {
        /// <summary>
        /// Get identity settings for a given outlet, with fallback:
        ///  - first try specific outletId
        ///  - then Global (OutletId == null)
        ///  - else return a new instance with defaults.
        /// </summary>
        Task<IdentitySettings> GetAsync(int? outletId, CancellationToken ct = default);

        /// <summary>
        /// Insert/update identity settings and sync via Outbox.
        /// </summary>
        Task SaveAsync(IdentitySettings settings, CancellationToken ct = default);
    }
}
