using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.DTO.Admin;

namespace Pos.Domain.Services.Admin
{
    /// <summary>
    /// Single source of truth for binding a physical machine (PC) to an outlet/counter.
    /// Client must supply the machine identity; this service never calls UI or OS APIs.
    /// </summary>
    public interface ICounterBindingService
    {
        Task<CounterBindingDto?> GetCurrentBindingAsync(string machineId, CancellationToken ct = default);

        /// <summary>
        /// Assigns (or reassigns) a machine to an outlet+counter.
        /// Enforces 1:1 both ways: one machine ⇄ one active counter binding.
        /// </summary>
        Task<CounterBindingDto> AssignAsync(
            string machineId,
            string machineName,
            int outletId,
            int counterId,
            CancellationToken ct = default);

        /// <summary>
        /// Removes any binding for the specified machine.
        /// </summary>
        Task UnassignAsync(string machineId, CancellationToken ct = default);
    }
}
