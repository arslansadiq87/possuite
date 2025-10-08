using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Shell; // for ViewTab
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Shell;

public sealed class DashboardVm : ObservableObject
{
    // ---------- Services ----------
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    private readonly AppState _st;
    private readonly IWindowNavigator _nav;
    private readonly IViewNavigator _views;     // single-shell view navigation (now tab-aware)
    private readonly IDialogService _dialogs;   // overlay-based confirms, etc.

    public DashboardVm(
        IDbContextFactory<PosClientDbContext> dbf,
        AppState st,
        IWindowNavigator nav,
        IViewNavigator views,
        IDialogService dialogs)                  // NEW: actually injected now
    {
        _dbf = dbf;
        _st = st;
        _nav = nav;
        _views = views;
        _dialogs = dialogs;

        // ---------- Defaults ----------
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

        // ---------- Tabs ----------
        Tabs = new ObservableCollection<ViewTab>();

        // ---------- Commands ----------
        ExitCmd = new RelayCommand(() => Application.Current.Shutdown());

        // Open modules as TABS (for views we already have)
        NewSaleCmd = new RelayCommand(OpenSalesTab);
        OpenReturnsCmd = new AsyncRelayCommand(() => NotImplementedAsync("Returns (stub)"));
        OpenPurchaseCmd = new RelayCommand(OpenPurchaseTab);
        OpenTransferCmd = new RelayCommand(OpenTransferTab);
        OpenProductsCmd = new RelayCommand(OpenProductsTab);
        OpenPartiesCmd = new RelayCommand(OpenPartiesTab);
        OpenOutletsCountersCmd = new RelayCommand(OpenOutletCountersTab);
        // Keep this one as a WINDOW for now (if you haven't converted to a view yet)
        //OpenOutletsCountersCmd = new RelayCommand(() => _nav.Open<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>());
        //OpenOutletsCountersCmd = new RelayCommand(() => _nav.Show<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>());

        OpenUsersCmd = new RelayCommand(OpenUsersTab);
        //OpenReportsCmd = new RelayCommand(OpenReportsTab);

        ReceiveDispatchCmd = new AsyncRelayCommand(() => NotImplementedAsync("Transfer receive (stub)"));

        CloseOverlayCmd = new RelayCommand(() => { IsOverlayOpen = false; OverlayView = null; });

        // Tab management
        CloseActiveTabCmd = new RelayCommand(_views.CloseActiveTab);
        CloseAllTabsCmd = new RelayCommand(CloseAllTabs);
        CloseOthersCmd = new RelayCommand(CloseOtherTabs);
        CloseLeftCmd = new RelayCommand(CloseLeftTabs);
        CloseRightCmd = new RelayCommand(CloseRightTabs);

        RefreshCmd = new AsyncRelayCommand(RefreshAsync);
    }

    private async Task NotImplementedAsync(string feature)
    {
        // Yes/No confirm
        //var ok = await _dialogs.ConfirmAsync($"{feature} — feature not implemented yet. Continue anyway?", "POS");
        //if (!ok) return;

        //// Examples of the new API:
        await _dialogs.AlertAsync($"{feature} — feature not implemented yet. Continue anyway?", "POS"); // OK

        //var res = await _dialogs.ShowAsync(
        //    "Close all tabs?",
        //    "Close All",
        //    DialogButtons.YesNoCancel);

        //if (res == DialogResult.Yes)
        //{
        //    Tabs.Clear();
        //    ActiveTab = null;
        //    TransferTabVisible = false;
        //}
    }


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

    // ---------- Back-compat (single-shell content) ----------
    // Keep this so any old bindings/XAML compile even if center host is now TabControl.
    private object _currentView;
    public object CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }

    // ---------- Overlay region (for in-shell dialogs) ----------
    private bool _isOverlayOpen;
    public bool IsOverlayOpen { get => _isOverlayOpen; set => SetProperty(ref _isOverlayOpen, value); }

    private object _overlayView;
    public object OverlayView { get => _overlayView; set => SetProperty(ref _overlayView, value); }

    // ---------- Contextual tabs ----------
    private bool _transferTabVisible;
    public bool TransferTabVisible { get => _transferTabVisible; set => SetProperty(ref _transferTabVisible, value); }

    // ---------- Tabbed documents ----------
    public ObservableCollection<ViewTab> Tabs { get; }

    private ViewTab _activeTab;
    public ViewTab ActiveTab
    {
        get => _activeTab;
        set => SetProperty(ref _activeTab, value);
    }

    // ---------- Commands (bind these in XAML) ----------
    public IRelayCommand ExitCmd { get; }
    public IRelayCommand NewSaleCmd { get; }
    public IRelayCommand OpenReturnsCmd { get; }
    public IRelayCommand OpenPurchaseCmd { get; }
    public IRelayCommand OpenTransferCmd { get; }
    public IRelayCommand OpenProductsCmd { get; }
    public IRelayCommand OpenPartiesCmd { get; }
    public IRelayCommand OpenOutletsCountersCmd { get; }
    public IRelayCommand OpenUsersCmd { get; }
    public IRelayCommand OpenReportsCmd { get; }
    public IRelayCommand ReceiveDispatchCmd { get; }
    public IRelayCommand CloseOverlayCmd { get; }
    public IRelayCommand CloseActiveTabCmd { get; }
    public IRelayCommand CloseAllTabsCmd { get; }
    public IRelayCommand CloseOthersCmd { get; }
    public IRelayCommand CloseLeftCmd { get; }
    public IRelayCommand CloseRightCmd { get; }
    public IAsyncRelayCommand RefreshCmd { get; }

    // ---------- Openers => Tabs ----------
    private void OpenTransferTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Inventory.TransferView>("Stock Transfer", "Transfer");

    private void OpenSalesTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Sales.SaleInvoiceView>("Sales", "Sales");

    private void OpenPurchaseTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Purchases.PurchaseView>("Purchases", "Purchase");

    private void OpenProductsTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Admin.ProductsItemsView>("Products", "Products");

    private void OpenPartiesTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Admin.PartiesView>("Parties", "Parties");

    private void OpenOutletCountersTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>("Outlets", "Outlets");

    private void OpenUsersTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Admin.UsersView>("Users", "Users");

    //private void OpenReportsTab()
    //    => _views.OpenTab<Pos.Client.Wpf.Windows.Reports.ReportsView>("Reports", "Reports");

    // ---------- Bulk tab ops ----------
    private async void CloseAllTabs()
    {
        if (Tabs.Count == 0) return;
        var ok = await _dialogs.ConfirmAsync($"Close all {Tabs.Count} tabs?", "Close All");
        if (!ok) return;
        Tabs.Clear();
        ActiveTab = null;
        TransferTabVisible = false;
    }

    private void CloseOtherTabs()
    {
        if (ActiveTab is null) return;
        for (int i = Tabs.Count - 1; i >= 0; i--)
        {
            var t = Tabs[i];
            if (!ReferenceEquals(t, ActiveTab)) Tabs.RemoveAt(i);
        }
    }

    private void CloseLeftTabs()
    {
        if (ActiveTab is null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        for (int i = idx - 1; i >= 0; i--) Tabs.RemoveAt(i);
    }

    private void CloseRightTabs()
    {
        if (ActiveTab is null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        for (int i = Tabs.Count - 1; i > idx; i--) Tabs.RemoveAt(i);
    }

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
