using System.Threading;
using System.Threading.Tasks;

namespace Pos.Domain.Services
{
    /// <summary>Read-only lookups for outlet/counter display names.</summary>
    public interface IOutletReadService
    {
        Task<string> GetOutletNameAsync(int outletId, CancellationToken ct = default);
        Task<string> GetCounterNameAsync(int counterId, CancellationToken ct = default);
    }
}
