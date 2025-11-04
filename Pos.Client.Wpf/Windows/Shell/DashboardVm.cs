using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Shell; // for ViewTab
using Pos.Persistence;
using System;                        // for Action
using System.Linq; // add this
using Pos.Client.Wpf.Windows.Sales; // for TillSessionSummaryVm
using Pos.Domain.Entities; // for TillSession


namespace Pos.Client.Wpf.Windows.Shell;

public sealed class DashboardVm : ObservableObject
{
    private readonly ITillService _till;

    // ---------- Services ----------
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    private readonly AppState _st;
    private readonly IWindowNavigator _nav;
    private readonly IViewNavigator _views;     // single-shell view navigation (now tab-aware)
    private readonly IDialogService _dialogs;   // overlay-based confirms, etc.
    // keep your existing TillStatus string property — do NOT switch to [ObservableProperty]
    private bool _isTillOpen;
    public bool IsTillOpen { get => _isTillOpen; set => SetProperty(ref _isTillOpen, value); }
    private readonly Func<int, int, int, TillSessionSummaryWindow> _tillSummaryWindowFactory;


    public DashboardVm(
        IDbContextFactory<PosClientDbContext> dbf,
        AppState st,
        IWindowNavigator nav,
        IViewNavigator views,
        IDialogService dialogs,
        ITillService till,
        Func<int, int, int, TillSessionSummaryWindow> tillSummaryWindowFactory)   // <-- add this

    {
        _dbf = dbf;
        _st = st;
        _nav = nav;
        _views = views;
        _dialogs = dialogs;
        _till = till;                      // <-- NEW


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
        OpenSaleCenterCmd = new RelayCommand(OpenSaleCenterCmdTab);
        OpenPurchaseCmd = new RelayCommand(OpenPurchaseTab);
        OpenPurchaseInvoiceCenterCmd = new RelayCommand(OpenInvoiceCenterPurchaseTab);
        OpenTransferCmd = new RelayCommand(OpenTransferTab);
        OpenProductsCmd = new RelayCommand(OpenProductsTab);
        OpenPartiesCmd = new RelayCommand(OpenPartiesTab);
        OpenStaffCmd = new RelayCommand(OpenStaffTab);
        OpenOutletsCountersCmd = new RelayCommand(OpenOutletCountersTab);
        OpenWarehousesCmd = new RelayCommand(OpenWarehousesTab);
        OpenStockCheckCmd = new RelayCommand(StockCheckTab);
        // Keep this one as a WINDOW for now (if you haven't converted to a view yet)
        //OpenOutletsCountersCmd = new RelayCommand(() => _nav.Open<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>());
        //OpenOutletsCountersCmd = new RelayCommand(() => _nav.Show<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>());
        OpenUsersCmd = new RelayCommand(OpenUsersTab);
        //OpenReportsCmd = new RelayCommand(OpenReportsTab);
        // NEW till commands
        OpenTillCmd = new AsyncRelayCommand(OpenTillAsync);
        CloseTillCmd = new AsyncRelayCommand(CloseTillAsync);
        ReceiveDispatchCmd = new AsyncRelayCommand(() => NotImplementedAsync("Transfer receive (stub)"));
        CloseOverlayCmd = new RelayCommand(() => { IsOverlayOpen = false; OverlayView = null; });
        // Tab management
        CloseActiveTabCmd = new RelayCommand(_views.CloseActiveTab);
        CloseAllTabsCmd = new RelayCommand(CloseAllTabs);
        CloseOthersCmd = new RelayCommand(CloseOtherTabs);
        CloseLeftCmd = new RelayCommand(CloseLeftTabs);
        CloseRightCmd = new RelayCommand(CloseRightTabs);
        OpenOtherAccountsCmd = new RelayCommand(OpenOtherAccountsTab);
        OpenVoucherEditorCmd = new RelayCommand(OpenVouchersTab);
        //OpenOpeningBalanceCmd = new RelayCommand(() => _nav.Show<Pos.Client.Wpf.Windows.Accounting.OpeningBalanceWindow>());

        OpenChartOfAccountsCmd = new RelayCommand(OpenChartOfAccountsTab);

        OpenVoucherCenterCmd = new RelayCommand(OpenVoucherCenterTab);

        //    OpenChartOfAccountsCmd = new RelayCommand(() =>
        //_nav.Show<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsWindow>());

        //OpenVoucherEditorCmd = new RelayCommand(() =>
        //    _nav.Show<Pos.Client.Wpf.Windows.Accounting.VoucherEditorWindow>());
        OpenAccounteLedgerCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.AccountLedgerWindow>());

        OpenCashBookCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.CashBookWindow>());


        OpenPayrollCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.PayrollRunWindow>());

        OpenAttendanceCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.AttendancePunchWindow>());

        OpenArApReportCmd = new RelayCommand(() => _nav.Show<Pos.Client.Wpf.Windows.Accounting.ArApReportWindow>());




        RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        SyncTillUi();                      // <-- NEW
        _tillSummaryWindowFactory = tillSummaryWindowFactory;                  // <-- store it
        OpenTillSummaryCmd = new AsyncRelayCommand(OpenTillSummaryAsync);  // <-- ADD THIS
    }

    private async Task NotImplementedAsync(string feature)
    {
        await _dialogs.AlertAsync($"{feature} — feature not implemented yet. Continue anyway?", "POS"); // OK
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
    public IRelayCommand OpenSaleCenterCmd { get; }
    public IRelayCommand OpenPurchaseCmd { get; }
    public IRelayCommand OpenPurchaseInvoiceCenterCmd { get; }
    public IRelayCommand OpenTransferCmd { get; }
    public IRelayCommand OpenProductsCmd { get; }
    public IRelayCommand OpenPartiesCmd { get; }
    public IRelayCommand OpenOutletsCountersCmd { get; }
    public IRelayCommand OpenWarehousesCmd { get; }
    public IRelayCommand OpenUsersCmd { get; }
    public IRelayCommand OpenReportsCmd { get; }
    public IRelayCommand ReceiveDispatchCmd { get; }
    public IRelayCommand CloseOverlayCmd { get; }
    public IRelayCommand CloseActiveTabCmd { get; }
    public IRelayCommand CloseAllTabsCmd { get; }
    public IRelayCommand CloseOthersCmd { get; }
    public IRelayCommand CloseLeftCmd { get; }
    public IRelayCommand CloseRightCmd { get; }

    public IRelayCommand OpenStockCheckCmd { get; }

    public IRelayCommand OpenChartOfAccountsCmd { get; }
    public IRelayCommand OpenVouchersCmd { get; }
    public IRelayCommand OpenVoucherEditorCmd { get; }
    public IRelayCommand OpenVoucherCenterCmd { get; }
    public IRelayCommand OpenPayrollCmd { get; }

    public IRelayCommand OpenAccounteLedgerCmd { get; }
    public IRelayCommand OpenCashBookCmd { get; }
    public IRelayCommand OpenArApReportCmd { get; }
    
    public IRelayCommand OpenAttendanceCmd { get; }
    //public IRelayCommand OpenOpeningBalanceCmd { get; }
    public IRelayCommand OpenStaffCmd { get; }
    public IRelayCommand OpenOtherAccountsCmd { get; }
    public IAsyncRelayCommand RefreshCmd { get; }
    public IAsyncRelayCommand OpenTillSummaryCmd { get; }
    // NEW commands
    public IAsyncRelayCommand OpenTillCmd { get; }
    public IAsyncRelayCommand CloseTillCmd { get; }

    
    // Notify views if you want (optional hook from window/tabs)
    public event Action? TillChanged;

    // --- Helper: try to activate an existing tab by Tag (preferred) or Title ---
    private bool TryActivateTab<TView>()
    {
        var existing = Tabs.FirstOrDefault(t =>
            t.Content?.GetType() == typeof(TView));

        if (existing is null) return false;

        ActiveTab = existing;
        return true;
    }



    // ---------- Openers => Tabs ----------
    private void OpenTransferTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Inventory.TransferEditorView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Inventory.TransferEditorView>("Stock Transfer", "Transfer");
    }
        

    private void OpenSalesTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Sales.SaleInvoiceView>("Sales", "Sales");

    private void OpenSaleCenterCmdTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Sales.InvoiceCenterView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Sales.InvoiceCenterView>("Sale Center", "Sale Center");
    }

    private void OpenPurchaseTab()
        => _views.OpenTab<Pos.Client.Wpf.Windows.Purchases.PurchaseView>("Purchases", "Purchase");

    private void OpenInvoiceCenterPurchaseTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Purchases.PurchaseCenterView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Purchases.PurchaseCenterView>("Purchase Center", "Purchase Center");
    }
        

    private void OpenProductsTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.ProductsItemsView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.ProductsItemsView>("Products", "Products");
    }


    private void OpenPartiesTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.PartiesView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.PartiesView>("Parties", "Parties");
    }

    private void OpenStaffTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.StaffView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.StaffView>("Staff", "Staff");
    }

    private void OpenOtherAccountsTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.OtherAccountsView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.OtherAccountsView>("Other Accounts", "Other Accounts");
    }

    private void OpenVouchersTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.VoucherEditorView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.VoucherEditorView>("Vourchers", "Vouchers");
    }

    private void OpenChartOfAccountsTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.ChartOfAccountsView>("Chart Of Accounts", "Chart Of Accounts");
    }

    private void OpenOutletCountersTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.OutletsCountersView>("Outlets", "Outlets");
    }
    private void OpenWarehousesTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.WarehousesView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.WarehousesView>("Warehouses", "Warehouses");
    }

    private void OpenUsersTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Admin.UsersView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Admin.UsersView>("Users", "Users");
    }

    private void StockCheckTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Sales.StockReportView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Sales.StockReportView>("Stock Check", "Stock Check");
    }

    private void OpenVoucherCenterTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.VoucherCenterView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.VoucherCenterView>("Voucher Center", "Voucher Center");
    }

    //private void OpenReportsTab()
    //    => _views.OpenTab<Pos.Client.Wpf.Windows.Reports.ReportsView>("Reports", "Reports");

    // ---------- Bulk tab ops ----------a
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
        SyncTillUi();                      // <-- NEW
    }

    

    private async Task OpenTillSummaryAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();

        var open = await db.Set<TillSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.OutletId == CurrentOutletId
                                   && t.CounterId == CurrentCounterId
                                   && t.CloseTs == null); // <-- use CloseTs (not ClosedAt)

        if (open is null)
        {
            await _dialogs.AlertAsync("No open till session. Open the till first.", "Till Summary");
            return;
        }

        var win = _tillSummaryWindowFactory(open.Id, CurrentOutletId, CurrentCounterId);

        // give it proper owner/centering (same as your navigator would)
        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowInTaskbar = false;
        }

        win.ShowDialog();
    }



private void SyncTillUi()
    {
        TillStatus = _till.GetStatusText();
        IsTillOpen = _till.IsTillOpen();
    }

    private async Task OpenTillAsync()
    {
        if (await _till.OpenTillAsync())
        {
            SyncTillUi();
            TillChanged?.Invoke();
        }
    }

    private async Task CloseTillAsync()
    {
        if (await _till.CloseTillAsync())
        {
            SyncTillUi();
            TillChanged?.Invoke();
        }
    }

}
