using System.Threading;
using System.Threading.Tasks;

namespace Pos.Domain.Services
{
    public interface IProductMediaService
    {
        // Sync for converter use (WPF converters are sync)
        string? GetPrimaryThumbPath(int productId);

        // Async for viewmodels / background work
        Task<string?> GetPrimaryThumbPathAsync(int productId, CancellationToken ct = default);
    }
}
