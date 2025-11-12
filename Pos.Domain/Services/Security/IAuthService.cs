using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.DTO.Security;

namespace Pos.Domain.Services.Security
{
    public interface IAuthService
    {
        Task<LoginResultDto> LoginAsync(string username, string password, CancellationToken ct = default);
        Task LogoutAsync(CancellationToken ct = default);
        Task<UserInfoDto?> GetCurrentUserAsync(CancellationToken ct = default);
    }
}
