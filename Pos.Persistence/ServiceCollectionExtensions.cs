using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Domain.Services;
using Pos.Persistence.Boot;
using Pos.Persistence.Diagnostics; // <-- add

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
            // Interceptor must be resolved from DI
            //services.AddSingleton<SqlLoggerInterceptor>();

            // Use the overload that gives us the ServiceProvider so we can pull ILoggerFactory + interceptor
            services.AddDbContextFactory<PosClientDbContext>((sp, o) =>
            {
                //var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                o.UseSqlite(connectionString);

                // Diagnostics
                //o.EnableDetailedErrors();
#if DEBUG
                //o.EnableSensitiveDataLogging();
                //// Send EF logs through the app logger pipeline (Debug/Console already added in App.xaml.cs)
                //o.UseLoggerFactory(loggerFactory);
                //o.LogTo(
                //    (msg) => loggerFactory.CreateLogger("EF").LogInformation("{Msg}", msg),
                //    LogLevel.Information,
                //    DbContextLoggerOptions.Category | DbContextLoggerOptions.SingleLine);
#endif
                // Log every SQL + parameters
                //o.AddInterceptors(sp.GetRequiredService<SqlLoggerInterceptor>());

                // Reasonable default; override per-query when you need tracking
                //o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

                // Allow external customization
                configure?.Invoke(o);
            });

            services.AddSingleton<IDbBootstrapper, SqliteDbBootstrapper>();
            return services;
        }
    }
}
