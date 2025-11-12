using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.DTO;

namespace Pos.Domain.Services
{
    public interface ITillReadService
    {
        Task<TillSessionSummaryDto> GetSessionSummaryAsync(int tillId, CancellationToken ct = default);
    }
}
