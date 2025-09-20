using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Domain.Entities; // <-- add

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Auth backed by the local SQLite client DB (PosClientDbContext).
    /// </summary>
    public class AuthService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public AuthService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        // NEW: cache the signed-in user (for post-login consumers)
        public User? CurrentUser { get; private set; }   // <-- add

        public async Task<(bool ok, string? error)> LoginAsync(string username, string password)
        {
            using var db = await _dbf.CreateDbContextAsync();

            // Look up active user
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user is null) return (false, "Invalid username or inactive user.");

            // Verify password hash (BCrypt)
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return (false, "Wrong password.");

            // NEW: remember who logged in
            CurrentUser = user;                           // <-- add
            return (true, null);
        }

        // (Optional) logout helper
        public void Logout() => CurrentUser = null;
    }
}
