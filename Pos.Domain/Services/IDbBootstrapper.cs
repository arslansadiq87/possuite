using System.Threading;
using System.Threading.Tasks;

namespace Pos.Domain.Services
{
    /// <summary>
    /// Handles database initialization work that must run before the app shell shows:
    /// - Ensure database file exists
    /// - Apply EF migrations
    /// - Set SQLite PRAGMAs (WAL, sync, busy_timeout)
    /// - Seed data and one-off data fixups
    /// </summary>
    public interface IDbBootstrapper
    {
        Task EnsureClientDbReadyAsync(CancellationToken ct = default);
    }
}
