// Pos.Persistence/Services/IdentitySettingsService.cs
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class IdentitySettingsService : IIdentitySettingsService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public IdentitySettingsService(
            IDbContextFactory<PosClientDbContext> dbf,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<IdentitySettings> GetAsync(int? outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // 1) Outlet-specific row
            var outletRow = await db.IdentitySettings
                .AsNoTracking()
                .Where(x => x.OutletId == outletId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            // 2) Global row
            var globalRow = await db.IdentitySettings
                .AsNoTracking()
                .Where(x => x.OutletId == null)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            // 3) New instance if nothing exists
            return outletRow ?? globalRow ?? new IdentitySettings { OutletId = outletId };
        }

        public async Task SaveAsync(IdentitySettings settings, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            settings.UpdatedAtUtc = DateTime.UtcNow;

            if (settings.Id == 0)
            {
                await db.IdentitySettings.AddAsync(settings, ct);
            }
            else
            {
                db.IdentitySettings.Update(settings);
            }

            // Persist first to get stable Id/PublicId
            await db.SaveChangesAsync(ct);

            // Enqueue for sync (IdentitySettings inherits BaseEntity ⇒ OutboxWriter works)
            await _outbox.EnqueueUpsertAsync(db, settings, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
