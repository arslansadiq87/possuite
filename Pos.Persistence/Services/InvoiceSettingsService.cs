using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class InvoiceSettingsService : IInvoiceSettingsService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public InvoiceSettingsService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<(InvoiceSettings Settings, InvoiceLocalization Local)> GetAsync(
            int? outletId, string? lang, CancellationToken ct = default)
        {
            lang ??= "en";
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Try outlet-scoped, then global
            var outletRow = await db.InvoiceSettings
                .Include(x => x.Localizations)
                .AsNoTracking()
                .Where(x => x.OutletId == outletId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var globalRow = await db.InvoiceSettings
                .Include(x => x.Localizations)
                .AsNoTracking()
                .Where(x => x.OutletId == null)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var settings = outletRow ?? globalRow ?? new InvoiceSettings { OutletId = outletId };

            // Resolve localization: lang → "en" → sensible default
            var loc = (settings.Localizations?.FirstOrDefault(x => x.Lang == lang)
                      ?? settings.Localizations?.FirstOrDefault(x => x.Lang == "en"))
                      ?? new InvoiceLocalization { Lang = lang, Footer = "Thank you for shopping with us!" };

            return (settings, loc);
        }

        public async Task<string?> GetPrinterAsync(int? outletId, CancellationToken ct = default)
        {
            var (s, _) = await GetAsync(outletId, "en", ct);
            return s.PrinterName;
        }

        public async Task<int> GetPaperWidthAsync(int? outletId, CancellationToken ct = default)
        {
            var (s, _) = await GetAsync(outletId, "en", ct);
            return s.PaperWidthMm <= 0 ? 80 : s.PaperWidthMm;
        }

        public async Task<int?> GetSalesCardClearingAccountIdAsync(int? outletId, CancellationToken ct = default)
        {
            var (s, _) = await GetAsync(outletId, "en", ct);
            return s.SalesCardClearingAccountId;
        }

        public async Task<int?> GetPurchaseBankAccountIdAsync(int? outletId, CancellationToken ct = default)
        {
            var (s, _) = await GetAsync(outletId, "en", ct);
            return s.PurchaseBankAccountId;
        }

        public async Task SaveAsync(
            InvoiceSettings settings,
            IEnumerable<InvoiceLocalization> localizations,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            settings.UpdatedAtUtc = DateTime.UtcNow;

            // Upsert main settings
            if (settings.Id == 0)
            {
                await db.InvoiceSettings.AddAsync(settings, ct);
            }
            else
            {
                db.InvoiceSettings.Update(settings);
            }

            // Upsert localizations (ensure FK)
            if (localizations is not null)
            {
                foreach (var loc in localizations)
                {
                    if (loc.Id == 0)
                    {
                        // attach via navigation; EF will set FK
                        loc.InvoiceSettings = settings;
                        await db.Set<InvoiceLocalization>().AddAsync(loc, ct);
                    }
                    else
                    {
                        db.Set<InvoiceLocalization>().Update(loc);
                    }
                }
            }

            // First save to get stable keys/rowversions
            await db.SaveChangesAsync(ct);

            // Enqueue to outbox BEFORE final save
            await _outbox.EnqueueUpsertAsync(db, settings, ct);

            if (localizations is not null)
            {
                foreach (var loc in localizations)
                {
                    await _outbox.EnqueueUpsertAsync(db, loc, ct);
                }
            }

            // Final save + commit
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
