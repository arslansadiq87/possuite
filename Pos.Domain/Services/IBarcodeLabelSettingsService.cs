using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IBarcodeLabelSettingsService
    {
        Task<BarcodeLabelSettings> GetAsync(int? outletId, CancellationToken ct = default);
        Task SaveAsync(BarcodeLabelSettings s, CancellationToken ct = default);
    }
}
