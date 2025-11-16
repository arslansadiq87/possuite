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
using Pos.Persistence.Sync;
using Pos.Client.Wpf.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Pos.Client.Wpf.Services.Sync; // ISyncService
using Pos.Domain.Services;
using Pos.Domain.Services.Hr;
using Pos.Domain.Services.Security;
using Pos.Domain.Services.Admin;
using Pos.Persistence.Services.Admin;
using Pos.Domain.Services.System;
using Pos.Persistence.Services.Systems;
using Pos.Domain.Services.Accounting;
using Pos.Persistence.Services.Accounting;
using Pos.Persistence.Services.Hr;

namespace Pos.Client.Wpf
{
    public partial class App : Application
    {

        public App()
        {

            // If your file has InitializeComponent(), keep it first.
            try { InitializeComponent(); } catch { /* ok if not generated */ }
            CrashReporter.Install(this);
            this.DispatcherUnhandledException += (s, e) =>
            {
                try { MessageBox.Show(e.Exception.ToString(), "UI Crash", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { /* swallow */ }
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                try { MessageBox.Show(ex?.ToString() ?? "Unknown crash", "App Crash", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { /* swallow */ }
            };
        }


        public static IServiceProvider Services { get; private set; } = null!;
        private CancellationTokenSource? _syncCts;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);


            ThemeManager.Current.ChangeTheme(Application.Current, "Light.Blue");

            // ----- BUILD DI FIRST (moved up) -----
            var sc = new ServiceCollection();
            sc.AddLogging(b =>
            {
                b.ClearProviders();          // stop Console/Debug spam providers if you added them
                                             // b.AddDebug();             // OPTIONAL: re-enable later if you want *your* ILogger logs

                b.SetMinimumLevel(LogLevel.None); // everything below Warning is dropped

                // Hard filters:
                b.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
                b.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                b.AddFilter("System.Net.Http", LogLevel.Error);     // drop Http info/diagnostics
                b.AddFilter("Microsoft", LogLevel.Warning);
                b.AddFilter("System", LogLevel.None);
            });
            // 1) Connection string
            var cs = DbPath.ConnectionString;
            var dbFile = DbPath.Get();
            // 2) Client persistence stack (DbContextFactory + bootstrapper)
            sc.AddClientSqlitePersistence(cs);

            // View navigation & dialogs
            sc.AddTransient<Windows.Shell.DashboardVm>();
            sc.AddTransient<Windows.Shell.DashboardWindow>();
            sc.AddSingleton<IViewNavigator, ViewNavigator>();
            sc.AddSingleton<IWindowNavigator, WindowNavigator>();
            sc.AddSingleton<IDialogService, DialogService>();
            sc.AddSingleton<IPaymentDialogService, PaymentDialogService>();
            // Features & services (your existing registrations, unchanged)
            sc.AddScoped<Pos.Persistence.Features.Transfers.ITransferService, Pos.Persistence.Features.Transfers.TransferService>();
            sc.AddScoped<ITransferQueries, TransferQueries>();
            sc.AddTransient<TransferCenterView>();
            // Read-only helpers (no EF in UI)
            sc.AddScoped<ILookupService, LookupService>();
            sc.AddScoped<IInventoryReadService, InventoryReadService>();
            sc.AddTransient<BarcodeLabelSettingsViewModel>();
            sc.AddSingleton<ResetStockService>();
            // 3) App state
            sc.AddSingleton<AppState>(AppState.Current);
            // NEW: machine/counter DI (you added these earlier)
            sc.AddSingleton<IMachineIdentityService, MachineIdentityService>();
            // 4) Windows (register them so we can resolve via DI)
            sc.AddTransient<LoginWindow>();
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

            sc.AddTransient<TillSessionSummaryWindow>();
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

            // Windows (transient)
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.VoucherEditorView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.PayrollRunWindow>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchView>(); // UserControl; may be hosted in a window
            
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AccountLedgerView>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.CashBookView>();
            // VMs
            sc.AddTransient<AccountLedgerVm>();
            sc.AddTransient<CashBookVm>();   // (for the other window as well)
            // ViewModels (scoped or transient)
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.VoucherEditorVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.PayrollRunVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.AttendancePunchWindow>();
            sc.AddTransient<InvoiceSettingsViewModel>();
            sc.AddSingleton<ILabelPrintService, LabelPrintServiceStub>();
            sc.AddScoped<IPurchaseCenterReadService, PurchaseCenterReadService>();
            sc.AddScoped<IPurchasesServiceFactory, PurchasesServiceFactory>();
            
            sc.AddScoped<ICoaService, CoaService>();
            sc.AddScoped<IOutletService, OutletService>();
            sc.AddTransient<VoucherCenterVm>();
            sc.AddTransient<VoucherCenterView>();
            
            sc.AddScoped<ISalesService, SalesService>();
            sc.AddScoped<IInvoiceService, InvoiceService>();
            //Manged services
            sc.AddScoped<IPurchaseReturnsService, PurchaseReturnsService>();
            sc.AddScoped<IProductMediaService, ProductMediaService>();
            sc.AddScoped<IPartyPostingService, PartyPostingService>();
            sc.AddScoped<IPayrollService, PayrollService>();
            sc.AddScoped<IStaffReadService, StaffReadService>();
            sc.AddScoped<IGlPostingService, GlPostingService>();
            sc.AddScoped<IGlPostingServiceDb, GlPostingService>();  // db-aware, used by persistence services
            sc.AddScoped<IGlReadService, GlReadService>();


            sc.AddSingleton<Pos.Domain.Services.Security.IAuthService, Pos.Persistence.Services.Security.AuthService>();
            sc.AddSingleton<Pos.Domain.Services.Security.IAuthorizationService, Pos.Persistence.Services.Security.AuthorizationService>();
            //sc.AddScoped<ICounterBindingService, CounterBindingService>();
            sc.AddScoped<ICounterBindingService, CounterBindingService>();

            sc.AddScoped<IOutletReadService, OutletReadService>();
            sc.AddScoped<ITillService, TillService>();
            sc.AddScoped<IArApQueryService, ArApQueryService>();
            sc.AddScoped<IAttendanceService, AttendanceService>();

            // Terminal context is provided from UI (ids only)
            sc.AddScoped<ITerminalContext, Pos.Client.Wpf.Services.TerminalContext>();
            //sc.AddScoped<IItemsReadService>(sp => sp.GetRequiredService<ItemsService>());
            sc.AddScoped<IReportsService, ReportsService>();
            sc.AddScoped<ITillReadService, TillReadService>();
            sc.AddSingleton<TillSessionSummaryVmFactory>();
            sc.AddTransient<Func<int, int, int, TillSessionSummaryWindow>>(sp =>
                (tillId, outletId, counterId) => new TillSessionSummaryWindow(tillId, outletId, counterId));
            sc.AddScoped<IPartyService, PartyService>();
            sc.AddScoped<IItemsReadService, ItemsService>();
            sc.AddScoped<IInvoiceSettingsService, InvoiceSettingsService>();
            sc.AddScoped<ICategoryService, CategoryService>();
            sc.AddScoped<IItemsWriteService, CatalogService>();
            sc.AddScoped<ICatalogService, CatalogService>();
            sc.AddScoped<IBrandService, BrandService>();
            sc.AddScoped<IBarcodeLabelSettingsService, BarcodeLabelSettingsService>();
            sc.AddScoped<IPartyLookupService, PartyLookupService>();
            sc.AddScoped<IOpeningStockService, OpeningStockService>();
            sc.AddScoped<IOtherAccountService, OtherAccountService>();
            sc.AddScoped<IOutletCounterService, OutletCounterService>();
            sc.AddScoped<ILookupService, LookupService>();
            sc.AddScoped<IPurchasesService, PurchasesService>();
            sc.AddScoped<Pos.Domain.Services.IStaffService, Pos.Persistence.Services.StaffService>();
            sc.AddScoped<ILedgerQueryService, LedgerQueryService>();
            sc.AddScoped<ICoaService, CoaService>();
            sc.AddScoped<IStockGuard, StockGuard>();
            sc.AddScoped<IUserAdminService, UserAdminService>();
            sc.AddSingleton<IUserPreferencesService, UserPreferencesService>();
            sc.AddScoped<IUserReadService, UserReadService>();
            sc.AddScoped<IVoucherCenterService, VoucherCenterService>();
            sc.AddScoped<IWarehouseService, WarehouseService>();

            // VM & window
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ArApReportVm>();
            sc.AddTransient<Pos.Client.Wpf.Windows.Accounting.ArApReportWindow>();
            // after other services
            sc.AddSingleton<Pos.Persistence.Sync.ISyncTokenService, Pos.Persistence.Sync.SyncTokenService>();
            sc.AddSingleton<Pos.Persistence.Sync.IOutboxWriter, Pos.Persistence.Sync.OutboxWriter>();
            //sc.AddScoped<IOutboxWriter, OutboxWriter>();
            sc.AddHttpClient<Pos.Client.Wpf.Services.Sync.ISyncHttp, Pos.Client.Wpf.Services.Sync.SyncHttp>(c =>
            {
                c.BaseAddress = new Uri("http://localhost:5089/"); // TODO: set
            });
            sc.AddSingleton<Pos.Client.Wpf.Services.Sync.ISyncService, Pos.Client.Wpf.Services.Sync.SyncService>();
            sc.AddLogging(b =>
            {
                b.ClearProviders();
                b.AddDebug();   // shows in VS Output window
                b.AddConsole(); // optional for console runs
                b.SetMinimumLevel(LogLevel.Information);
            });

            Services = sc.BuildServiceProvider();
            var prefs = Services.GetRequiredService<IUserPreferencesService>();
            try
            {
                var p = prefs.GetAsync().GetAwaiter().GetResult();  // sync call
                TimeService.SetTimeZone(p.DisplayTimeZoneId);
            }
            catch
            {
                TimeService.SetTimeZone(null);
            }

            // 5) Ensure DB + seed (delegated to persistence bootstrapper)
            try
            {
                using var scope = Services.CreateScope();
                var bootstrap = scope.ServiceProvider.GetRequiredService<IDbBootstrapper>();
                bootstrap.EnsureClientDbReadyAsync().GetAwaiter().GetResult(); // sync call in startup
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SQLite startup DB error:\n\n{ex}", "Startup Error");
                Shutdown();
                return;
            }
            // Login
            var oldMode = this.ShutdownMode;
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var login = Services.GetRequiredService<LoginWindow>();
            var ok = login.ShowDialog() == true;
            if (!ok)
            {
                Shutdown();
                return;
            }

            // Copy signed-in user to AppState
            _ = Task.Run(async () =>
            {
                var auth = App.Services.GetRequiredService<IAuthService>();
                var state = App.Services.GetRequiredService<AppState>();

                var user = await auth.GetCurrentUserAsync(CancellationToken.None);
                if (user is not null)
                {
                    state.CurrentUserId = user.Id;
                    state.CurrentUserName = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.Username
                        : user.FullName;
                }
                else
                {
                    state.CurrentUserId = state.CurrentUserId > 0 ? state.CurrentUserId : 0;
                    state.CurrentUserName = string.IsNullOrWhiteSpace(state.CurrentUserName) ? "admin" : state.CurrentUserName;
                }
            });

            // ----- NOW it’s safe to ensure counter binding (DI is ready) -----
            try
            {
                using var cts = new CancellationTokenSource();
                await EnsureCounterBindingAsync(cts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to resolve counter binding:\n\n" + ex.Message,
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // ---- START PERIODIC SYNC (after login + binding, before showing shell) ----
            try
            {
                var sync = Services.GetRequiredService<ISyncService>();
                _syncCts = new CancellationTokenSource();
                _ = RunSyncLoopAsync(sync, _syncCts.Token); // fire-and-forget
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SYNC] Could not start sync loop: " + ex);
            }
            // ---- END PERIODIC SYNC ----

            var shell = Services.GetRequiredService<Windows.Shell.DashboardWindow>();
            Application.Current.MainWindow = shell;
            shell.Show();
            this.ShutdownMode = oldMode; // e.g. back to OnMainWindowClose
        }

        private async Task EnsureCounterBindingAsync(CancellationToken ct)
        {
            var sp = Services;
            var midS = sp.GetRequiredService<IMachineIdentityService>();
            var svc = sp.GetRequiredService<ICounterBindingService>();

            var machineId = await midS.GetMachineIdAsync(ct);
            var machineName = await midS.GetMachineNameAsync(ct);

            var binding = await svc.GetCurrentBindingAsync(machineId, ct);
            if (binding is null)
            {
                // Show the assignment UI
                var view = sp.GetRequiredService<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>();
                var host = sp.GetRequiredService<Pos.Client.Wpf.Windows.Common.ViewHostWindow>();

                host.Title = "Outlets & Counters";
                host.SetView(view);
                host.ShowInTaskbar = true;

                MessageBox.Show(
                    "This PC is not assigned to any counter yet.\n\n" +
                    "Open the outlet, select a counter, and click 'Assign This PC'. Then close this window.",
                    "Counter Assignment Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                view.RequestClose += (_, __) =>
                {
                    host.DialogResult = true;
                    host.Close();
                };

                try
                {
                    host.ShowDialog();
                }
                catch (ArgumentException aex)
                {
                    MessageBox.Show(
                        "Could not open Outlets & Counters dialog:\n\n" + aex.Message,
                        "Window Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                binding = await svc.GetCurrentBindingAsync(machineId, ct);
                if (binding is null)
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

            var st = sp.GetRequiredService<AppState>();
            st.CurrentOutletId = binding.OutletId;
            st.CurrentCounterId = binding.CounterId;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _syncCts?.Cancel(); } catch { /* ignore */ }
            _syncCts?.Dispose();
            base.OnExit(e);
        }

        private static async Task RunSyncLoopAsync(ISyncService sync, CancellationToken ct)
        {
            // Small delay so UI settles
            await Task.Delay(TimeSpan.FromSeconds(2), ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await sync.PushAsync(ct);
                    await sync.PullAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break; // shutting down
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine("[SYNC] Error in loop: " + ex.Message);
                    // keep going
                }

                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }
}
