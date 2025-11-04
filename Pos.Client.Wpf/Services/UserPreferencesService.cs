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

        var m = string.IsNullOrWhiteSpace(p.MachineName) ? Environment.MachineName : p.MachineName;

        // Read without tracking so we can Update() a new instance safely
        var existing = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MachineName == m, ct);

        if (existing == null)
        {
            // brand-new row
            p.MachineName = m;             // ensure it’s set
            db.UserPreferences.Add(p);
        }
        else
        {
            // keep the key & principal columns, update the rest
            p.Id = existing.Id;            // PRESERVE KEY
            p.MachineName = existing.MachineName;

            // This marks all scalars as modified without trying to change the key
            db.UserPreferences.Update(p);
        }

        await db.SaveChangesAsync(ct);
    }

}
