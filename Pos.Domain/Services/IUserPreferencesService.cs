using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IUserPreferencesService
    {
        Task<UserPreference> GetAsync(CancellationToken ct = default);
        Task SaveAsync(UserPreference p, CancellationToken ct = default);
    }
}
