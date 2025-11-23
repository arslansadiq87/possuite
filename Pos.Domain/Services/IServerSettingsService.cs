using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Settings;

namespace Pos.Domain.Services
{
    public interface IServerSettingsService
    {
        Task<ServerSettings> GetAsync(CancellationToken ct);
        Task UpsertAsync(ServerSettings settings, CancellationToken ct);
    }
}
