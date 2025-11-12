using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Pos.Domain.Services;

namespace Pos.Persistence.Boot
{
    public sealed class SqliteDbBootstrapper : IDbBootstrapper
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ILogger<SqliteDbBootstrapper> _log;

        public SqliteDbBootstrapper(
            IDbContextFactory<PosClientDbContext> dbf,
            ILogger<SqliteDbBootstrapper> log)
        {
            _dbf = dbf;
            _log = log;
        }

        public async Task EnsureClientDbReadyAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // (Optional) log path if you have a helper for it
            try
            {
                var conn = db.Database.GetDbConnection();
                var path = conn.DataSource; // SQLite file path
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (!File.Exists(path))
                        _log.LogInformation("[DB] Creating new DB at: {DbPath}", path);
                    else
                        _log.LogInformation("[DB] Using existing DB at: {DbPath}", path);
                }
            }
            catch { /* non-fatal */ }

            // Apply migrations
            await db.Database.MigrateAsync(ct);

            // Set SQLite PRAGMAs (safe to run each startup)
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
                await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Setting PRAGMAs failed (ignored on non-SQLite providers).");
            }

            // Seed + data fixups (keep as your existing static helpers)
            try
            {
                Seed.Ensure(db);                 // your existing seeding
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Seeding failed.");
                throw;
            }

            try
            {
                DataFixups.NormalizeUsers(db);   // your existing fixups
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Data fixups failed (continuing).");
            }

            _log.LogInformation("Database bootstrap completed.");
        }
    }
}
