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
using Microsoft.Extensions.Logging;
using Pos.Persistence.Services;
using Pos.Persistence.Features.Transfers;
using ControlzEx.Theming;
using Pos.Client.Wpf.Windows.Inventory;
using Pos.Domain.Entities;
using Pos.Client.Wpf.Windows.Settings;
using Pos.Client.Wpf.Windows.Accounting;



namespace Pos.Client.Wpf
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.Current.ChangeTheme(Application.Current, "Light.Blue");

            //ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncAll;  // or SyncWithAppMode / SyncWithAccent
            //ThemeManager.Current.SyncTheme();
            //ThemeManager.Current.ChangeTheme(Application.Current, "Light.Blue");
            // 3) Keep every open window in lock-step:
            
            
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

            sc.AddTransient<Windows.Shell.DashboardVm>();
            sc.AddTransient<Windows.Shell.DashboardWindow>();

            // View navigation
            sc.AddSingleton<IViewNavigator, ViewNavigator>();
            sc.AddSingleton<IWindowNavigator, WindowNavigator>();
            sc.AddSingleton<IDialogService, DialogService>();
            sc.AddSingleton<IPaymentDialogService, PaymentDialogService>();
            sc.AddSingleton<ITillService, TillService>();
            sc.AddSingleton<ITerminalContext, TerminalContext>();
            // Party posting (build a DbContext instance from the factory for each use)
            sc.AddTransient<PartyPostingService>(sp =>
            {
                var dbf = sp.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                return new PartyPostingService(dbf.CreateDbContext());
            });
            // Party lookup (construct DbContext per resolve so queries use a fresh context)
            sc.AddTransient<PartyLookupService>(sp =>
            {
                var dbf = sp.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                return new PartyLookupService(dbf.CreateDbContext());
            });


            sc.AddSingleton<ResetStockService>();
            sc.AddSingleton<IUserPreferencesService, UserPreferencesService>();


            // 3) App services

            sc.AddSingleton<AppState>(AppState.Current);
            sc.AddSingleton<AuthService>();
            sc.AddSingleton<StockGuard>();            // ✅ Register here


            //sc.AddSingleton<CurrentUserService>();


            // NEW: machine/counter DI (you added these earlier)
            sc.AddSingleton<IMachineIdentityService, MachineIdentityService>();
            sc.AddScoped<CounterBindingService>();

            // 4) Windows (register them so we can resolve via DI)
            sc.AddTransient<LoginWindow>();
            
            // Views
            sc.AddTransient<SaleInvoiceView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.UsersView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>();
            sc.AddTransient<WarehousesView>();
            // NEW: OpeningStockView factory (parametrized)
            sc.AddTransient<Func<InventoryLocationType, int, string, Pos.Client.Wpf.Windows.Admin.OpeningStockView>>(sp =>
            {
                return (locType, locId, label) =>
                    new Pos.Client.Wpf.Windows.Admin.OpeningStockView(locType, locId, label);
            });
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.UserOutletAssignmentsWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.ProductsItemsView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Purchases.PurchaseView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Purchases.PurchaseCenterView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.PartiesView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.EditPartyWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.StaffView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Admin.StaffDialog>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Inventory.TransferEditorView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Sales.PayDialog>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Sales.StockReportView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Sales.InvoiceCenterView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Common.ViewHostWindow>();
            sc.AddScoped<Pos.Client.Wpf.Services.IStaffDirectory, Pos.Client.Wpf.Services.StaffDirectory>();
            sc.AddScoped<ICoaService, CoaService>();

            sc.AddTransient<TillSessionSummaryWindow>();
            sc.AddTransient<Func<int, int, int, TillSessionSummaryWindow>>(sp =>
            {
                var opts = sp.GetRequiredService<DbContextOptions<PosClientDbContext>>();
                return (tillId, outletId, counterId) =>
                    new TillSessionSummaryWindow(opts, tillId, outletId, counterId);
            });
            sc.AddTransient<OtherAccountsView>();
            sc.AddTransient<OtherAccountDialog>();



            // WINDOWS (register every window you resolve from DI)
            sc.AddTransient<BrandsWindow>();
            sc.AddTransient<EditBrandWindow>();
            sc.AddTransient<CategoriesWindow>();
            sc.AddTransient<EditCategoryWindow>();
            
            sc.AddTransient<EditWarehouseWindow>();
            sc.AddTransient<PreferencesViewModel>();
            sc.AddTransient<PreferencesPage>();
            //sc.AddTransient<Pos.Client.Wpf.Services.OpeningBalanceService>();
            //sc.AddTransient<Pos.Client.Wpf.Services.OpeningBalanceService>();


            // Windows (transient)
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.VoucherEditorWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.PayrollRunWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchView>(); // UserControl; may be hosted in a window

            // ViewModels (scoped or transient)
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.VoucherEditorVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.PayrollRunVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchWindow>();


            sc.AddSingleton<IInvoiceSettingsService, InvoiceSettingsService>();
            sc.AddTransient<InvoiceSettingsViewModel>();
            sc.AddTransient<BarcodeLabelSettingsViewModel>();
            sc.AddSingleton<IBarcodeLabelSettingsService, BarcodeLabelSettingsService>();
            sc.AddSingleton<ILabelPrintService, LabelPrintServiceStub>();

            sc.AddScoped<Pos.Persistence.Services.IOpeningStockService, Pos.Persistence.Services.OpeningStockService>();
            sc.AddScoped<CatalogService>();   // <-- register this
            sc.AddScoped<Pos.Persistence.Features.Transfers.ITransferService, Pos.Persistence.Features.Transfers.TransferService>();
            sc.AddScoped<ITransferQueries, TransferQueries>();
            sc.AddScoped<ItemsService>();
            sc.AddScoped<IGlPostingService, GlPostingService>();
            sc.AddScoped<IAttendanceService, AttendanceService>();
            sc.AddScoped<IPayrollService, PayrollService>();
            // after DbContext/ITerminalContext registrations
            sc.AddScoped<Pos.Client.Wpf.Services.ICoaService, Pos.Client.Wpf.Services.CoaService>();
            sc.AddScoped<Pos.Client.Wpf.Services.IOutletService, Pos.Client.Wpf.Services.OutletService>();

            // VM & window
            //sc.AddTransient<OpeningBalanceVm>();
            //sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.OpeningBalanceWindow>();

            // Posting service (if not already added earlier)
            //sc.AddTransient<Pos.Client.Wpf.Services.OpeningBalanceService>();


            //sc.AddTransient<UsersWindow>(sp =>
            //{
            //    var dbf = sp.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            //    var cu = sp.GetRequiredService<CurrentUserService>().CurrentUser;
            //    return new UsersWindow(dbf, cu);
            //});
            sc.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddDebug();   // shows in VS Output window
                b.AddConsole(); // optional for console runs
                b.SetMinimumLevel(LogLevel.Information);
            });

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
                    // Resolve the usercontrol and host window
                    var view = Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>();
                    var host = Services.GetRequiredService<Pos.Client.Wpf.Windows.Common.ViewHostWindow>();

                    host.Title = "Outlets & Counters";
                    host.SetView(view);
                    host.ShowInTaskbar = true;

                    MessageBox.Show(
                        "This PC is not assigned to any counter yet.\n\n" +
                        "Open the outlet, select a counter, and click 'Assign This PC'. Then close this window.",
                        "Counter Assignment Required",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // If you added the close event, wire it:
                    view.RequestClose += (_, __) => { host.DialogResult = true; host.Close(); };

                    // Show modally
                    try
                    {
                        host.ShowDialog();
                    }
                    catch (ArgumentException aex)
                    {
                        MessageBox.Show("Could not open Outlets & Counters dialog:\n\n" + aex.Message,
                                        "Window Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }

                    // Retry binding after the dialog closes
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
                var st = Services.GetRequiredService<AppState>();
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

            //var dash = Services.GetRequiredService<DashboardWindow>();
            //MainWindow = dash;
            //dash.Show();

            // using System.Windows;
            // using Pos.Client.Wpf.Windows.Shell;

            //var shell = Services.GetRequiredService<DashboardWindow>(); // exact type
            //Application.Current.MainWindow = shell;                     // RibbonWindow : Window
            //shell.Show();
            var shell = Services.GetRequiredService<Windows.Shell.DashboardWindow>();
            Application.Current.MainWindow = shell;
            shell.Show();

            this.ShutdownMode = oldMode; // e.g. back to OnMainWindowClose
        }

        

    }
}
