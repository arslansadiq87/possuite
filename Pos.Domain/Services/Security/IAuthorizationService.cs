using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.DTO.Security;

namespace Pos.Domain.Services.Security
{
    public interface IAuthorizationService
    {
        Task<bool> HasAsync(UserInfoDto? user, Perm permission, int? outletId = null, CancellationToken ct = default);

        Task<bool> IsAdminAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default);
        Task<bool> IsManagerOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default);
        Task<bool> IsSupervisorOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default);
        Task<bool> IsCashierOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default);
    }
}
