using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Domain.Services;
using Pos.Persistence.Boot;

namespace Pos.Persistence
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the client-side persistence stack for SQLite, including DbContextFactory and the bootstrapper.
        /// Call this from the WPF composition root.
        /// </summary>
        public static IServiceCollection AddClientSqlitePersistence(
            this IServiceCollection services,
            string connectionString,
            Action<DbContextOptionsBuilder>? configure = null)
        {
            services.AddDbContextFactory<PosClientDbContext>(o =>
            {
                o.UseSqlite(connectionString);
#if DEBUG
                o.EnableSensitiveDataLogging();
                o.LogTo(msg => System.Diagnostics.Debug.WriteLine(msg),
                    LogLevel.Information);
#endif
                configure?.Invoke(o);
            });

            services.AddSingleton<IDbBootstrapper, SqliteDbBootstrapper>();
            return services;
        }
    }
}
