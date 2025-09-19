// Pos.Client.Wpf/App.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Shell;   // << add
using Pos.Client.Wpf.Windows.Sales;
using Pos.Persistence;

namespace Pos.Client.Wpf
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var sc = new ServiceCollection();

            // 1) Connection string
            var cs = DbPath.ConnectionString;
            var dbFile = DbPath.Get();

            // 2) DbContextFactory (client DB)
            sc.AddDbContextFactory<PosClientDbContext>(o =>
                o.UseSqlite(cs)
                 .EnableSensitiveDataLogging()
                 .LogTo(msg => Debug.WriteLine(msg))
            );

            // 3) App services
            sc.AddSingleton<AppState>(AppState.Current);
            sc.AddSingleton<AuthService>();

            // 4) Windows (register them so we can resolve via DI)
            sc.AddTransient<LoginWindow>();
            sc.AddTransient<DashboardWindow>();
            sc.AddTransient<SaleInvoiceWindow>();


            Services = sc.BuildServiceProvider();

            // 5) Ensure DB + seed
            try
            {
                using var scope = Services.CreateScope();
                var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                using var db = dbf.CreateDbContext();

                if (!File.Exists(dbFile))
                    Debug.WriteLine($"[DB] Creating new DB at: {dbFile}");
                else
                    Debug.WriteLine($"[DB] Using existing DB at: {dbFile}");

                db.Database.Migrate();
                Seed.Ensure(db);  // make sure this seeds bcrypt admin/admin123
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SQLite startup DB error:\n\n{ex}", "Startup Error");
                Shutdown();
                return;
            }

            // 6) Show login after DI is ready
            var login = Services.GetRequiredService<LoginWindow>();
            login.Show();
        }
    }
}
