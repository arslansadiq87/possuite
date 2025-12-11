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
using Pos.Domain.Services;
//using Pos.Persistence.Migrations;
using Microsoft.VisualBasic;
using Pos.Domain.Models.Till;

namespace Pos.Client.Wpf.Windows.Shell;

public sealed class DashboardVm : ObservableObject
{
    private readonly ITillService _till;
    private readonly IOutletReadService _outletRead;      // add
    private readonly AppState _st;
    private readonly IWindowNavigator _nav;
    private readonly IViewNavigator _views;     // single-shell view navigation (now tab-aware)
    private readonly IDialogService _dialogs;   // overlay-based confirms, etc.
    private bool _isTillOpen;
    public bool IsTillOpen { get => _isTillOpen; set => SetProperty(ref _isTillOpen, value); }
    private readonly Func<int, int, int, TillSessionSummaryWindow> _tillSummaryWindowFactory;


    public DashboardVm(
         AppState st,
         IWindowNavigator nav,
         IViewNavigator views,
         IDialogService dialogs,
         ITillService till,
         IOutletReadService outletRead,
         Func<int, int, int, TillSessionSummaryWindow> tillSummaryWindowFactory)
    {
        //_dbf = dbf;
        _st = st;
        _nav = nav;
        _views = views;
        _dialogs = dialogs;
        _till = till;
        _outletRead = outletRead;
             

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
        OpenTransferCenterCmd = new RelayCommand(OpenTransferCenterTab);
        OpenLabelPrintCmd = new RelayCommand(OpenLabelPrintTab);        // NEW

        OpenProductsCmd = new RelayCommand(OpenProductsTab);
        OpenPartiesCmd = new RelayCommand(OpenPartiesTab);
        OpenStaffCmd = new RelayCommand(OpenStaffTab);
        OpenOutletsCountersCmd = new RelayCommand(OpenOutletCountersTab);
        OpenWarehousesCmd = new RelayCommand(OpenWarehousesTab);
        OpenStockCheckCmd = new RelayCommand(StockCheckTab);
        OpenUsersCmd = new RelayCommand(OpenUsersTab);
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
        OpenCashBookCmd = new RelayCommand((OpenCashBookTab));

            
        OpenChartOfAccountsCmd = new RelayCommand(OpenChartOfAccountsTab);

        OpenVoucherCenterCmd = new RelayCommand(OpenVoucherCenterTab);

        OpenAccounteLedgerCmd = new RelayCommand((OpenAccounteLedgerTab));
        OpenPurchaserLedgerCmd = new RelayCommand(OpenPurchaserLedgerTab);   // NEW
        OpenStockValuationCmd = new RelayCommand(OpenStockValuationTab);    // NEW



        OpenPayrollCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.PayrollRunWindow>());

        OpenAttendanceCmd = new RelayCommand(() =>
            _nav.Show<Pos.Client.Wpf.Windows.Accounting.AttendancePunchWindow>());

        OpenArApReportCmd = new RelayCommand(() => _nav.Show<Pos.Client.Wpf.Windows.Accounting.ArApReportWindow>());
        OpenReportsCmd = new RelayCommand(() => _ = NotImplementedAsync("Reports"));
        OpenVouchersCmd = new RelayCommand(OpenVouchersTab);
        RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        _ = SyncTillUiAsync();
        _tillSummaryWindowFactory = tillSummaryWindowFactory;                  // <-- store it
        OpenTillSummaryCmd = new AsyncRelayCommand(OpenTillSummaryAsync);  // <-- ADD THIS
    }

    private async Task NotImplementedAsync(string feature)
    {
        await _dialogs.AlertAsync($"{feature} — feature not implemented yet. Continue anyway?", "POS"); // OK
    }

    // ---------- Top strip / status ----------
    private string _outletName = "-";
    public string OutletName { get => _outletName; set => SetProperty(ref _outletName, value); }

    private string _counterName = "-";
    public string CounterName { get => _counterName; set => SetProperty(ref _counterName, value); }

    private string _tillStatus = "-";
    public string TillStatus { get => _tillStatus; set => SetProperty(ref _tillStatus, value); }

    private string _onlineText = "-";
    public string OnlineText { get => _onlineText; set => SetProperty(ref _onlineText, value); }

    private string _lastSyncText = "-";
    public string LastSyncText { get => _lastSyncText; set => SetProperty(ref _lastSyncText, value); }

    public string CurrentUserName => _st.CurrentUserName;
    public int CurrentOutletId => _st.CurrentOutletId;
    public int CurrentCounterId => _st.CurrentCounterId;

    // ---------- KPIs ----------
    private string _todaySalesFormatted = "-";
    public string TodaySalesFormatted { get => _todaySalesFormatted; set => SetProperty(ref _todaySalesFormatted, value); }

    private string _todaySalesInvoicesText = "-";
    public string TodaySalesInvoicesText { get => _todaySalesInvoicesText; set => SetProperty(ref _todaySalesInvoicesText, value); }

    private string _todayReturnsFormatted = "-";
    public string TodayReturnsFormatted { get => _todayReturnsFormatted; set => SetProperty(ref _todayReturnsFormatted, value); }

    private string _todayReturnsCountText = "-";
    public string TodayReturnsCountText { get => _todayReturnsCountText; set => SetProperty(ref _todayReturnsCountText, value); }

    private string _shiftCashFormatted = "-";
    public string ShiftCashFormatted { get => _shiftCashFormatted; set => SetProperty(ref _shiftCashFormatted, value); }

    private string _shiftHint = "-";
    public string ShiftHint { get => _shiftHint; set => SetProperty(ref _shiftHint, value); }

    // ---------- Back-compat (single-shell content) ----------
    // Keep this so any old bindings/XAML compile even if center host is now TabControl.
    private object? _currentView;
    public object? CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }
    // ---------- Overlay region (for in-shell dialogs) ----------
    private bool _isOverlayOpen;
    public bool IsOverlayOpen { get => _isOverlayOpen; set => SetProperty(ref _isOverlayOpen, value); }

    private object? _overlayView;
    public object? OverlayView { get => _overlayView; set => SetProperty(ref _overlayView, value); }
    // ---------- Contextual tabs ----------
    private bool _transferTabVisible;
    public bool TransferTabVisible { get => _transferTabVisible; set => SetProperty(ref _transferTabVisible, value); }

    // ---------- Tabbed documents ----------
    public ObservableCollection<ViewTab> Tabs { get; }

    private ViewTab? _activeTab;
    public ViewTab? ActiveTab
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
    public IRelayCommand OpenTransferCenterCmd { get; }
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
    public IRelayCommand OpenLabelPrintCmd { get; }          // NEW

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

    public IRelayCommand OpenPurchaserLedgerCmd { get; }   // NEW
    public IRelayCommand OpenStockValuationCmd { get; }    // NEW


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

    void OpenTransferCenterTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Inventory.TransferCenterView>())
            return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Inventory.TransferCenterView>("Transfer Center", "Transfer Center");
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

    private void OpenCashBookTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.CashBookView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.CashBookView>("Cash Book", "Cash Book");
    }

    private void OpenAccounteLedgerTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.AccountLedgerView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.AccountLedgerView>("Account Ledger", "Account Ledger");
    }

    private void OpenLabelPrintTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Inventory.LabelPrintView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Inventory.LabelPrintView>(
            "Print Labels",
            "Print Labels");
    }

    private void OpenPurchaserLedgerTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Accounting.PurchaserLedgerView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Accounting.PurchaserLedgerView>(
            "Supplier Ledger",    // title shown on tab header
            "Supplier Ledger");   // optional tag/category
    }

    private void OpenStockValuationTab()
    {
        if (TryActivateTab<Pos.Client.Wpf.Windows.Sales.StockValuationView>()) return;
        _views.OpenTab<Pos.Client.Wpf.Windows.Sales.StockValuationView>(
            "Stock Valuation",    // title shown on tab header
            "Stock Valuation");   // optional tag/category
    }



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
        if (CurrentOutletId > 0)
            OutletName = await _outletRead.GetOutletNameAsync(CurrentOutletId) ?? $"Outlet #{CurrentOutletId}";
        else
            OutletName = "-";

        if (CurrentCounterId > 0)
            CounterName = await _outletRead.GetCounterNameAsync(CurrentCounterId) ?? $"Counter #{CurrentCounterId}";
        else
            CounterName = "-";
        TillStatus = "Till: —";
        OnlineText = "Offline";
        LastSyncText = "Last sync: —";

        // notify if you show username in the bar
        OnPropertyChanged(nameof(CurrentUserName));
        await SyncTillUiAsync();
    }

    private async Task OpenTillSummaryAsync()
    {
        var status = await _till.GetStatusAsync();

        // If not open, show message and bail.
        if (!status.IsOpen || status.TillSessionId is null)
        {
            await _dialogs.AlertAsync("No open till session. Open the till first.", "Till Summary");
            return;
        }

        // Otherwise open the summary window for the current (open) till session.
        var win = _tillSummaryWindowFactory(status.TillSessionId.Value, CurrentOutletId, CurrentCounterId);

        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowInTaskbar = false;
        }

        win.ShowDialog();
    }

    private async Task SyncTillUiAsync()
    {
        var status = await _till.GetStatusAsync();
        TillStatus = status.Text;
        IsTillOpen = status.IsOpen;
    }

    private async Task OpenTillAsync()
    {
        var input = Interaction.InputBox("Enter OPENING FLOAT:", "Open Till", "0.00");
                if (!decimal.TryParse(input, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.CurrentCulture, out var openingFloat))
                    {
            await _dialogs.AlertAsync("Invalid amount. Till not opened.", "Open Till");
                        return;
                    }
        
                try
        {
            var res = await _till.OpenTillAsync(openingFloat);
            await _dialogs.AlertAsync($"Till opened. Id={res.TillSessionId}", "Till");
            await SyncTillUiAsync();
            TillChanged?.Invoke();
                    }
                catch (Exception ex)
        {
            await _dialogs.AlertAsync(ex.Message, "Open Till");
                    }
        }

    private async Task CloseTillAsync()
    {
        // 1) Preview — show expected cash + totals
        TillClosePreviewDto p;
        try
        {
            p = await _till.GetClosePreviewAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(ex.Message, "Close Till");
            return;
        }

        var previewText =
            $"=== Till Close Preview ===\n" +
            $"Opening Float  : {p.OpeningFloat:0.00}\n" +
            $"Sales Total    : {p.SalesTotal:0.00}\n" +
            $"Returns Total  : {p.ReturnsTotalAbs:0.00}\n" +
            $"Net Total      : {p.NetTotal:0.00}\n" +
            $"Expected Cash  : {p.ExpectedCash:0.00}\n\n" +
            $"Enter DECLARED CASH (default is Expected Cash).";

        // 2) Prompt for Declared Cash with expected as default
        var inputDefault = p.ExpectedCash.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            previewText, "Close Till (Z Report)", inputDefault);

        if (!decimal.TryParse(input, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.CurrentCulture, out var declaredCash) &&
            !decimal.TryParse(input, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.InvariantCulture, out declaredCash))
        {
            await _dialogs.AlertAsync("Invalid amount. Till not closed.", "Close Till");
            return;
        }

        // 3) Finalize close
        try
        {
            var z = await _till.CloseTillAsync(declaredCash);

            var report =
                $"=== Z REPORT (Till {z.TillSessionId}) ===\n" +
                $"Closed (local) : {z.ClosedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                $"Opening Float  : {z.OpeningFloat:0.00}\n" +
                $"Sales Total    : {z.SalesTotal:0.00}\n" +
                $"Returns Total  : {z.ReturnsTotalAbs:0.00}\n" +
                $"Net Total      : {z.NetTotal:0.00}\n" +
                $"Expected Cash  : {z.ExpectedCash:0.00}\n" +
                $"Declared Cash  : {z.DeclaredCash:0.00}\n" +
                $"Over/Short     : {z.OverShort:+0.00;-0.00;0.00}";

            await _dialogs.AlertAsync(report, "Z Report");

            await SyncTillUiAsync();
            TillChanged?.Invoke();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(ex.Message, "Close Till");
        }
    }


}
