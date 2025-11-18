using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;          // ← interface lives in Domain
using Pos.Persistence;
using Pos.Persistence.Sync;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public sealed class BarcodeLabelSettingsService : IBarcodeLabelSettingsService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public BarcodeLabelSettingsService(
            IDbContextFactory<PosClientDbContext> dbf,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<BarcodeLabelSettings> GetAsync(int? outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Prefer outlet-specific; fall back to global; otherwise defaults
            var outletRow = await db.BarcodeLabelSettings
                .AsNoTracking()
                .Where(x => x.OutletId == outletId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (outletRow is not null) return outletRow;

            var globalRow = await db.BarcodeLabelSettings
                .AsNoTracking()
                .Where(x => x.OutletId == null)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            return globalRow ?? new BarcodeLabelSettings { OutletId = outletId };
        }

        public async Task SaveAsync(BarcodeLabelSettings s, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            s.UpdatedAtUtc = DateTime.UtcNow;

            if (s.Id == 0)
            {
                await db.BarcodeLabelSettings.AddAsync(s, ct);
            }
            else
            {
                db.BarcodeLabelSettings.Update(s);
            }

            // Persist first to ensure Ids exist for outbox payloads
            await db.SaveChangesAsync(ct);

            // Outbox for sync (enqueue, then final save)
            //await _outbox.EnqueueUpsertAsync(db, s, ct);
            //await db.SaveChangesAsync(ct);
        }
    }
}
