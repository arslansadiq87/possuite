using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;
using Pos.Domain.Settings;
using Pos.Domain.Utils;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class InvoiceSettingsScopedService : IInvoiceSettingsScopedService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public InvoiceSettingsScopedService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<InvoiceSettingsScoped> GetGlobalAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var row = await db.InvoiceSettingsScoped.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutletId == null, ct);
            return row ?? new InvoiceSettingsScoped { OutletId = null };
        }

        public async Task<InvoiceSettingsScoped> GetForOutletAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var row = await db.InvoiceSettingsScoped.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutletId == outletId, ct);
            return row ?? new InvoiceSettingsScoped { OutletId = outletId };
        }

        /// <summary>
        /// Upsert for either Global (OutletId == null) or Outlet scope.
        /// Never modifies the primary key. If the "principal" (OutletId) differs, we delete+insert.
        /// </summary>
        public async Task UpsertAsync(InvoiceSettingsScoped model, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existing = await db.InvoiceSettingsScoped
                .FirstOrDefaultAsync(x => x.OutletId == model.OutletId, ct);

            if (existing is null)
            {
                // New row. Ensure PK is add-generated (if configured).
                // Do NOT carry a random Id from the VM.
                model.Id = 0;
                model.UpdatedAtUtc = DateTime.UtcNow;

                db.InvoiceSettingsScoped.Add(model);
                await db.SaveChangesAsync(ct);

                // enqueue sync
                var outletId = model.OutletId ?? 0;
                var key = GuidUtility.FromString($"{nameof(InvoiceSettingsScoped)}:{outletId}");
                await _outbox.EnqueueUpsertAsync(db, nameof(InvoiceSettingsScoped), key, model, ct);
                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
                return;
            }

            // Same scope (OutletId matches) -> update NON-KEY props only
            // Copy incoming values, but DO NOT let EF think the key changed
            // Same scope -> update ONLY non-key props
            var entry = db.Entry(existing);

            // copy scalar props except keys
            foreach (var p in entry.Properties)
            {
                if (p.Metadata.IsKey())
                    continue;

                // pull value from the incoming model by property name
                var name = p.Metadata.Name;
                var incomingProp = typeof(InvoiceSettingsScoped).GetProperty(name);
                if (incomingProp is null) continue;

                var val = incomingProp.GetValue(model);
                p.CurrentValue = val;
            }

            // never modify keys
            entry.Property(x => x.Id).IsModified = false;
            entry.Property(x => x.OutletId).IsModified = false;

            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);


            // enqueue sync
            {
                var outletId = existing.OutletId ?? 0;
                var key = GuidUtility.FromString($"{nameof(InvoiceSettingsScoped)}:{outletId}");
                await _outbox.EnqueueUpsertAsync(db, nameof(InvoiceSettingsScoped), key, existing, ct);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        }

        /// <summary>
        /// Explicit save for Global; identical semantics to UpsertAsync but forces OutletId == null.
        /// </summary>
        public async Task SaveGlobalAsync(InvoiceSettingsScoped model, CancellationToken ct = default)
        {
            model.OutletId = null; // force global scope
            await UpsertAsync(model, ct);
        }
    }
}
