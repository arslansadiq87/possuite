using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;
using Pos.Domain.Settings;

namespace Pos.Persistence.Services;

public class InvoiceSettingsLocalService : IInvoiceSettingsLocalService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;

    public InvoiceSettingsLocalService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    public async Task<InvoiceSettingsLocal> GetForCounterAsync(int counterId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var row = await db.InvoiceSettingsLocals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CounterId == counterId, ct);

        return row ?? new InvoiceSettingsLocal
        {
            CounterId = counterId,
            PrinterName = null,
            LabelPrinterName = null,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<InvoiceSettingsLocal> UpsertAsync(InvoiceSettingsLocal model, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.InvoiceSettingsLocals
            .FirstOrDefaultAsync(x => x.CounterId == model.CounterId, ct);

        if (existing is null)
        {
            model.UpdatedAtUtc = DateTime.UtcNow;
            db.InvoiceSettingsLocals.Add(model);
            await db.SaveChangesAsync(ct);
            return model;
        }

        existing.PrinterName = model.PrinterName;
        existing.LabelPrinterName = model.LabelPrinterName;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return existing;
    }


    public async Task<InvoiceSettingsLocal> GetForCounterWithFallbackAsync(int? counterId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        // 1) Exact counter
        if (counterId.GetValueOrDefault() > 0)
        {
            var exact = await db.InvoiceSettingsLocals.AsNoTracking()
                .Where(s => s.CounterId == counterId!.Value)
                .OrderByDescending(s => s.UpdatedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (exact is not null) return exact;

            // Same outlet (any counter)
            var outletId = await db.Counters.AsNoTracking()
                .Where(c => c.Id == counterId!.Value)
                .Select(c => c.OutletId)
                .FirstOrDefaultAsync(ct);

            if (outletId > 0)
            {
                var sameOutlet = await (from s in db.InvoiceSettingsLocals.AsNoTracking()
                                        join c in db.Counters.AsNoTracking() on s.CounterId equals c.Id
                                        where c.OutletId == outletId
                                        orderby s.UpdatedAtUtc descending
                                        select s)
                                       .FirstOrDefaultAsync(ct);
                if (sameOutlet is not null) return sameOutlet;
            }
        }

        // 2) Any latest
        var any = await db.InvoiceSettingsLocals.AsNoTracking()
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        // 3) Default empty
        return any ?? new InvoiceSettingsLocal
        {
            CounterId = counterId ?? 0,
            
        };
    }
}
