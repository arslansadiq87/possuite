using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Till;

namespace Pos.Domain.Services
{
    public interface ITillService
    {
        Task<TillOpenResultDto> OpenTillAsync(decimal openingFloat, CancellationToken ct = default);
        Task<TillCloseResultDto> CloseTillAsync(decimal declaredCash, CancellationToken ct = default);

        Task<TillStatusDto> GetStatusAsync(CancellationToken ct = default);
        Task<bool> IsTillOpenAsync(CancellationToken ct = default);
        Task<TillClosePreviewDto> GetClosePreviewAsync(CancellationToken ct = default);
    }
}
