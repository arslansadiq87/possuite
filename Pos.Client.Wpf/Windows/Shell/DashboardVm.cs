using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Shell;

public sealed class DashboardVm : ObservableObject
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    private readonly AppState _st;
    private readonly IWindowNavigator _nav;

    public DashboardVm(IDbContextFactory<PosClientDbContext> dbf, AppState st, IWindowNavigator nav)
    {
        _dbf = dbf;
        _st = st;
        _nav = nav;

        // defaults
        OutletName = "-";
        CounterName = "-";
        TillStatus = "Till: —";
        OnlineText = "Offline";
        LastSyncText = "Last sync: —";
        TodaySalesFormatted = "₨ 0";
        TodaySalesInvoicesText = "0 invoices";
        TodayReturnsFormatted = "₨ 0";
        TodayReturnsCountText = "0 returns";
        ShiftCashFormatted = "₨ 0";
        ShiftHint = "—";

        // Commands (no source generators)
        ExitCmd = new RelayCommand(() => Application.Current.Shutdown());
        NewSaleCmd = new RelayCommand(() => { _nav.Show<Windows.Sales.SaleInvoiceWindow>(); });
        OpenReturnsCmd = new RelayCommand(() => NotImplemented("Returns window"));
        OpenPurchaseCmd = new RelayCommand(() => { _nav.Show<Windows.Purchases.PurchaseWindow>(); });
        OpenTransferCmd = new RelayCommand(() => { _nav.Show<Windows.Inventory.TransferEditorWindow>();});
        OpenProductsCmd = new RelayCommand(() => { _nav.Show<Windows.Admin.ProductsItemsWindow>(); });
        OpenSuppliersCmd = new RelayCommand(() => NotImplemented("Suppliers window"));
        OpenOutletsCountersCmd = new RelayCommand(() => _nav.Show<Windows.Admin.OutletsCountersWindow>());
        OpenUsersCmd = new RelayCommand(() => { _nav.Show<Windows.Admin.UsersWindow>(); });
        OpenReportsCmd = new RelayCommand(() => NotImplemented("Reports window"));
        ReceiveDispatchCmd = new RelayCommand(() => NotImplemented("Transfer receive window"));
        RefreshCmd = new AsyncRelayCommand(RefreshAsync);
    }

    private static void NotImplemented(string feature) =>
        MessageBox.Show($"{feature} not wired yet.", "POS", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---------- Top strip / status ----------
    private string _outletName;
    public string OutletName { get => _outletName; set => SetProperty(ref _outletName, value); }

    private string _counterName;
    public string CounterName { get => _counterName; set => SetProperty(ref _counterName, value); }

    private string _tillStatus;
    public string TillStatus { get => _tillStatus; set => SetProperty(ref _tillStatus, value); }

    private string _onlineText;
    public string OnlineText { get => _onlineText; set => SetProperty(ref _onlineText, value); }

    private string _lastSyncText;
    public string LastSyncText { get => _lastSyncText; set => SetProperty(ref _lastSyncText, value); }

    public string CurrentUserName => _st.CurrentUserName;
    public int CurrentOutletId => _st.CurrentOutletId;
    public int CurrentCounterId => _st.CurrentCounterId;

    // ---------- KPIs ----------
    private string _todaySalesFormatted;
    public string TodaySalesFormatted { get => _todaySalesFormatted; set => SetProperty(ref _todaySalesFormatted, value); }

    private string _todaySalesInvoicesText;
    public string TodaySalesInvoicesText { get => _todaySalesInvoicesText; set => SetProperty(ref _todaySalesInvoicesText, value); }

    private string _todayReturnsFormatted;
    public string TodayReturnsFormatted { get => _todayReturnsFormatted; set => SetProperty(ref _todayReturnsFormatted, value); }

    private string _todayReturnsCountText;
    public string TodayReturnsCountText { get => _todayReturnsCountText; set => SetProperty(ref _todayReturnsCountText, value); }

    private string _shiftCashFormatted;
    public string ShiftCashFormatted { get => _shiftCashFormatted; set => SetProperty(ref _shiftCashFormatted, value); }

    private string _shiftHint;
    public string ShiftHint { get => _shiftHint; set => SetProperty(ref _shiftHint, value); }

    // ---------- Dashboard host (optional) ----------
    private object _currentView;
    public object CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }

    // ---------- Contextual tabs ----------
    private bool _transferTabVisible;
    public bool TransferTabVisible { get => _transferTabVisible; set => SetProperty(ref _transferTabVisible, value); }

    // ---------- Commands (bind these in XAML) ----------
    public IRelayCommand ExitCmd { get; }
    public IRelayCommand NewSaleCmd { get; }
    public IRelayCommand OpenReturnsCmd { get; }
    public IRelayCommand OpenPurchaseCmd { get; }
    public IRelayCommand OpenTransferCmd { get; }
    public IRelayCommand OpenProductsCmd { get; }
    public IRelayCommand OpenSuppliersCmd { get; }
    public IRelayCommand OpenOutletsCountersCmd { get; }
    public IRelayCommand OpenUsersCmd { get; }
    public IRelayCommand OpenReportsCmd { get; }
    public IRelayCommand ReceiveDispatchCmd { get; }
    public IAsyncRelayCommand RefreshCmd { get; }

    // ---------- Data refresh ----------
    public async Task RefreshAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();

        if (CurrentOutletId > 0)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == CurrentOutletId);
            OutletName = outlet?.Name ?? $"Outlet #{CurrentOutletId}";
        }
        else OutletName = "-";

        if (CurrentCounterId > 0)
        {
            var counter = await db.Counters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CurrentCounterId);
            CounterName = counter?.Name ?? $"Counter #{CurrentCounterId}";
        }
        else CounterName = "-";

        TillStatus = "Till: —";
        OnlineText = "Offline";
        LastSyncText = "Last sync: —";

        // notify if you show username in the bar
        OnPropertyChanged(nameof(CurrentUserName));
    }
}
