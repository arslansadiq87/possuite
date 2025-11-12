using System.Threading;
using System.Threading.Tasks;

namespace Pos.Domain.Services.System
{
    public interface IMachineIdentityService
    {
        /// <summary>
        /// Returns a stable machine ID stored under a shared system data directory.
        /// Creates and persists it if missing.
        /// </summary>
        Task<string> GetMachineIdAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns the OS machine name.
        /// </summary>
        Task<string> GetMachineNameAsync(CancellationToken ct = default);
    }
}
