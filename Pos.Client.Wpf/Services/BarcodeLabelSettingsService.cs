using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Settings;

public interface IBarcodeLabelSettingsService
{
    Task<BarcodeLabelSettings> GetAsync(int? outletId, CancellationToken ct = default);
    Task SaveAsync(BarcodeLabelSettings s, CancellationToken ct = default);
}

public class BarcodeLabelSettingsService : IBarcodeLabelSettingsService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    public BarcodeLabelSettingsService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    public async Task<BarcodeLabelSettings> GetAsync(int? outletId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        // outlet-specific
        var outletRow = await db.BarcodeLabelSettings
            .Where(x => x.OutletId == outletId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (outletRow != null) return outletRow;

        // global
        var globalRow = await db.BarcodeLabelSettings
            .Where(x => x.OutletId == null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return globalRow ?? new BarcodeLabelSettings();
    }

    public async Task SaveAsync(BarcodeLabelSettings s, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (s.Id == 0) db.BarcodeLabelSettings.Add(s);
        else db.BarcodeLabelSettings.Update(s);

        s.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
