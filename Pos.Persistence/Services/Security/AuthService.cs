using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.DTO.Security;
using Pos.Domain.Services.Security;
using Pos.Persistence;

namespace Pos.Persistence.Services.Security
{
    public sealed class AuthService : IAuthService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private UserInfoDto? _currentUser;

        public AuthService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<LoginResultDto> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var user = await db.Users
                .AsNoTracking()
                .Include(u => u.UserOutlets) // <-- bring outlet roles
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);

            if (user is null)
                return LoginResultDto.Fail("Invalid username or inactive user.");

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return LoginResultDto.Fail("Wrong password.");

            var outletRoles = user.UserOutlets
                .Select(uo => new UserOutletRoleDto { OutletId = uo.OutletId, Role = (int)uo.Role })
                .ToList()
                .AsReadOnly();

            _currentUser = new UserInfoDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                IsActive = user.IsActive,
                Role = user.Role,           // legacy/global baseline
                IsGlobalAdmin = user.IsGlobalAdmin,  // new global bypass
                OutletRoles = outletRoles
            };

            return LoginResultDto.Success(_currentUser);
        }

        public Task LogoutAsync(CancellationToken ct = default)
        {
            _currentUser = null;
            return Task.CompletedTask;
        }

        public Task<UserInfoDto?> GetCurrentUserAsync(CancellationToken ct = default)
            => Task.FromResult(_currentUser);
    }
}
