using Microsoft.EntityFrameworkCore;

namespace Pos.Persistence.Sync;

public interface ISyncTokenService
{
    Task<long> NextTokenAsync(PosClientDbContext db, CancellationToken ct = default);
}

public sealed class SyncTokenService : ISyncTokenService
{
    private static long _lastGenerated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static readonly object _lock = new();

    public Task<long> NextTokenAsync(PosClientDbContext db, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // always monotonic and unique even within same DbContext or rollback
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (now <= _lastGenerated)
                _lastGenerated++;

            _lastGenerated = now > _lastGenerated ? now : _lastGenerated;
            return Task.FromResult(_lastGenerated);
        }
    }
}
