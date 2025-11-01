using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

public interface IUserPreferencesService
{
    Task<UserPreference> GetAsync(CancellationToken ct = default);
    Task SaveAsync(UserPreference p, CancellationToken ct = default);
}

public sealed class UserPreferencesService : IUserPreferencesService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    public UserPreferencesService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    public async Task<UserPreference> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var m = Environment.MachineName;
        var p = await db.UserPreferences.FirstOrDefaultAsync(x => x.MachineName == m, ct);
        return p ?? new UserPreference { MachineName = m };
    }

    public async Task SaveAsync(UserPreference p, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.UserPreferences.FirstOrDefaultAsync(x => x.MachineName == p.MachineName, ct);
        if (existing == null) db.UserPreferences.Add(p);
        else db.Entry(existing).CurrentValues.SetValues(p);
        await db.SaveChangesAsync(ct);
    }
}
