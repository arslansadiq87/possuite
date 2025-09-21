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
using Pos.Client.Wpf.Windows.Admin;

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

            // NEW: machine/counter DI (you added these earlier)
            sc.AddSingleton<IMachineIdentityService, MachineIdentityService>();
            sc.AddScoped<CounterBindingService>();

            // 4) Windows (register them so we can resolve via DI)
            sc.AddTransient<LoginWindow>();
            sc.AddTransient<DashboardWindow>();
            sc.AddTransient<SaleInvoiceWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.UsersWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.UserOutletAssignmentsWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.ProductsItemsWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.SuppliersWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.CustomersWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Purchases.PurchaseWindow>();
            // WINDOWS (register every window you resolve from DI)
            sc.AddTransient<BrandsWindow>();
            sc.AddTransient<EditBrandWindow>();
            sc.AddTransient<CategoriesWindow>();
            sc.AddTransient<EditCategoryWindow>();

                       

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
                DataFixups.NormalizeUsers(db);  // <-- add this line

            }
            catch (Exception ex)
            {
                MessageBox.Show($"SQLite startup DB error:\n\n{ex}", "Startup Error");
                Shutdown();
                return;
            }

            // App.xaml.cs  (inside OnStartup, right before showing login)
            var oldMode = this.ShutdownMode;
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var login = Services.GetRequiredService<LoginWindow>();
            var ok = login.ShowDialog() == true;
            if (!ok)
            {
                Shutdown();
                return;
            }

            // NEW: copy the signed-in user into AppState so bindings have a value
            var auth = App.Services.GetRequiredService<AuthService>();
            var stForUser = App.Services.GetRequiredService<AppState>();
            if (auth?.CurrentUser != null)
            {
                stForUser.CurrentUserId = auth.CurrentUser.Id;
                stForUser.CurrentUserName = string.IsNullOrWhiteSpace(auth.CurrentUser.DisplayName)
                    ? auth.CurrentUser.Username
                    : auth.CurrentUser.DisplayName;
            }
            else
            {
                // Fallback so UI isn’t blank even if AuthService didn’t populate yet
                stForUser.CurrentUserId = stForUser.CurrentUserId > 0 ? stForUser.CurrentUserId : 0;
                stForUser.CurrentUserName = string.IsNullOrWhiteSpace(stForUser.CurrentUserName) ? "admin" : stForUser.CurrentUserName;
            }
            // >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
            // HYDRATE AppState from the current PC's counter binding.
            // If not bound, open the Outlets & Counters window so the user can assign now.
            try
            {
                var binder = Services.GetRequiredService<CounterBindingService>();
                var binding = binder.GetCurrentBinding();

                if (binding == null)
                {
                    // No owner — the login window is gone and MainWindow isn't set yet.
                    var manage = Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>();
                    manage.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    manage.ShowInTaskbar = true; // since there's no main window yet

                    MessageBox.Show(
                        "This PC is not assigned to any counter yet.\n\n" +
                        "Open the outlet, select a counter, and click 'Assign This PC'. Then close the window.",
                        "Counter Assignment Required",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    try
                    {
                        // Show modally to block until user closes it
                        manage.ShowDialog();
                    }
                    catch (ArgumentException aex)
                    {
                        // Safety: if anything odd happens showing the dialog, surface the reason
                        MessageBox.Show("Could not open Outlets & Counters window:\n\n" + aex.Message,
                                        "Window Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }

                    // Try again after the window closes
                    binding = binder.GetCurrentBinding();
                    if (binding == null)
                    {
                        MessageBox.Show(
                            "No counter assignment was made.\n\n" +
                            "The app will exit. Open Admin → Outlets & Counters to assign later.",
                            "No Counter Assigned",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        Shutdown();
                        return;
                    }
                }

                // Success: lock AppState to bound outlet+counter
                var st = Services.GetRequiredService<AppState>(); // or AppState.Current
                st.CurrentOutletId = binding.OutletId;
                st.CurrentCounterId = binding.CounterId;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to resolve counter binding:\n\n" + ex.Message,
                                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }


            // <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

            var dash = Services.GetRequiredService<DashboardWindow>();
            MainWindow = dash;
            dash.Show();

            this.ShutdownMode = oldMode; // e.g. back to OnMainWindowClose
        }
    }
}
