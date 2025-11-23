using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;
using Pos.Domain.Settings;

namespace Pos.Persistence.Services
{
    public sealed class ServerSettingsService : IServerSettingsService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public ServerSettingsService(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        public async Task<ServerSettings> GetAsync(CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var row = await db.ServerSettings.FirstOrDefaultAsync(ct);
            if (row is not null) return row;

            // fallback safety: create default row Id=1 if not seeded
            row = new ServerSettings { Id = 1, AutoSyncEnabled = true, PushIntervalSec = 15, PullIntervalSec = 15 };
            db.ServerSettings.Add(row);
            await db.SaveChangesAsync(ct);
            return row;
        }

        public async Task UpsertAsync(ServerSettings settings, CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var existing = await db.ServerSettings.FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                if (settings.Id == 0) settings.Id = 1;
                db.ServerSettings.Add(settings);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(settings);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
