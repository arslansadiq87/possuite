// Pos.Domain/Services/IUserReadService.cs
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Read-only user access for UI/server without exposing EF.
    /// </summary>
    public interface IUserReadService
    {
        /// <summary>
        /// Returns a user by exact username match, or null if not found.
        /// </summary>
        Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    }
}
