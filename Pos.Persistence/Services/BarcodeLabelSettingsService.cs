using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;          // ← interface lives in Domain
using Pos.Persistence;
using Pos.Persistence.Sync;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Utils; // add at top for GuidUtility

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

            var outletRow = await db.BarcodeLabelSettings
                .AsNoTracking()
                .Where(x => x.OutletId == outletId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var globalRow = await db.BarcodeLabelSettings
                .AsNoTracking()
                .Where(x => x.OutletId == null)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            // ✅ choose the NEWER of the two if both exist
            var chosen =
                (outletRow, globalRow) switch
                {
                    (null, null) => null,
                    (null, var g) => g,
                    (var o, null) => o,
                    (var o, var g) => (o.UpdatedAtUtc >= g.UpdatedAtUtc) ? o : g
                };

            if (chosen == null)
            {
                // First-time defaults
                chosen = new BarcodeLabelSettings { OutletId = outletId, Dpi = 203 };
            }

            // ✅ normalize before returning so callers never get Dpi <= 0
            if (chosen.Dpi <= 0) chosen.Dpi = 203;

            return chosen;
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
            // Outbox for sync (deterministic key per outlet/global)
            var key = GuidUtility.FromString($"{nameof(BarcodeLabelSettings)}:{s.OutletId ?? 0}");
            await _outbox.EnqueueUpsertAsync(db, nameof(BarcodeLabelSettings), key, s, ct);
            await db.SaveChangesAsync(ct);
            // Outbox for sync (enqueue, then final save)
            //await _outbox.EnqueueUpsertAsync(db, s, ct);
            //await db.SaveChangesAsync(ct);
        }
    }
}
