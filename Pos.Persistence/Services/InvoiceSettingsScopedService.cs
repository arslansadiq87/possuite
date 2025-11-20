// Pos.Persistence/Services/InvoiceSettingsScopedService.cs
using System;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;
using Pos.Domain.Settings;


namespace Pos.Persistence.Services;
public sealed class InvoiceSettingsScopedService : IInvoiceSettingsScopedService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    public InvoiceSettingsScopedService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

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

    public async Task UpsertAsync(InvoiceSettingsScoped model, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var isGlobal = model.OutletId == null;

        var existing = await db.InvoiceSettingsScoped
            .FirstOrDefaultAsync(x => x.OutletId == model.OutletId, ct);

        if (existing is null)
            db.InvoiceSettingsScoped.Add(model);
        else
        {
            db.Entry(existing).CurrentValues.SetValues(model);
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
