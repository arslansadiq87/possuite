using System.Windows.Input;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Sales;
using System.Windows.Media;
using System.Windows.Controls;
using Pos.Client.Wpf.Debugging;
using Pos.Client.Wpf.Windows.Settings;
using Pos.Persistence.Services;


namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class DashboardWindow : Fluent.RibbonWindow
    {
        private IInputElement? _focusBeforeOverlay;

        private readonly DashboardVm _vm;
        private readonly IViewNavigator _views;

        public DashboardWindow(DashboardVm vm, IViewNavigator views)
        {
            InitializeComponent();
            #if DEBUG
            //FocusTracer.Attach(this, "SHELL");
            #endif


            _vm = vm;
            _views = views;
            DataContext = _vm;

            // ADD THIS LINE:
            InitBackstage();

            Loaded += (_, __) =>
            {
                _views.Attach(_vm);
                // Optional: land on a default view (e.g., Reports or a Home view)
                // _views.SetRoot<Windows.Reports.ReportsView>();
                _ = _vm.RefreshAsync();
            };
            // Fire OnActivated() when the user changes the selected tab via UI
            if (DocumentTabs != null)
            {
                DocumentTabs.SelectionChanged += (_, __) =>
                {
                    if (DocumentTabs.SelectedContent is Pos.Client.Wpf.Infrastructure.IRefreshOnActivate r)
                        r.OnActivated();
                };
            }
            

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape && _vm.IsOverlayOpen)
                {
                    _views.HideOverlay();
                    e.Handled = true;
                }
            };
        }

        private void InitBackstage()
        {
            // construct the page from DI so its VM and services are resolved
            var prefs = App.Services.GetRequiredService<PreferencesPage>();
            BackstagePreferencesTab.Content = prefs;
        }


        private void OverlayLayer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (OverlayLayer.IsVisible)
            {
                // overlay opened → remember where we were
                _focusBeforeOverlay = Keyboard.FocusedElement;
                return;
            }

            // overlay closed → clear stale focus then restore
            Keyboard.ClearFocus();

            void FocusActiveTabAndScan()
            {
                if (DocumentTabs?.SelectedContent is not FrameworkElement content) return;

                // make selected tab content the logical/keyboard focus owner first
                DocumentTabs.Focus();
                content.Focus();

                // Sales view special case
                if (content is Pos.Client.Wpf.Windows.Sales.SaleInvoiceView sale)
                {
                    sale.RestoreFocusToScan();          // must be public (you already have it)
                    return;
                }

                // Generic fallback: find a "ScanText" in the tab content
                var scan = FindByName<TextBox>(content, "ScanText");
                if (scan != null)
                {
                    Keyboard.Focus(scan);
                    scan.CaretIndex = scan.Text?.Length ?? 0;
                }
            }

            // If the previously focused element is still valid, try it first
            if (TryRestorePreviousFocus(_focusBeforeOverlay))
            {
                _focusBeforeOverlay = null;
                return;
            }

            // Re-assert focus twice to beat late focusers (Ribbon, animations, etc.)
            Dispatcher.BeginInvoke(new Action(FocusActiveTabAndScan),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Dispatcher.BeginInvoke(new Action(FocusActiveTabAndScan),
                System.Windows.Threading.DispatcherPriority.Background);

            _focusBeforeOverlay = null;
        }

        private static bool TryRestorePreviousFocus(IInputElement? prev)
        {
            if (prev is FrameworkElement fe && fe.IsVisible && fe.IsEnabled && fe.IsLoaded)
            {
                Keyboard.Focus(fe); fe.Focus(); return true;
            }
            if (prev is FrameworkContentElement fce && fce.IsEnabled)
            { Keyboard.Focus(fce); return true; }
            return false;
        }

        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T fe && fe.Name == name) return fe;
                var r = FindByName<T>(c, name);
                if (r != null) return r;
            }
            return null;
        }


        private async void ResetStockButton_Click(object sender, RoutedEventArgs e)
        {
            // ✅ Close the backstage *by name* so dialogs aren’t hidden underneath
            if (MainBackstage != null && MainBackstage.IsOpen)
                MainBackstage.IsOpen = false;
            var dialogs = App.Services.GetRequiredService<IDialogService>();
            var reset = App.Services.GetRequiredService<ResetStockService>();

            var ok1 = await dialogs.ConfirmAsync(
                "This will DELETE:\n• Sales & Sale Lines (incl. returns)\n• Purchases, Purchase Lines & Payments (incl. returns)\n• Transfers (Stock Docs with DocType=Transfer) & their lines\n• Stock ledger entries for the above (Sale/Purchase/Transfer)\n\n" +
                "It WILL NOT touch Opening Stock or master data (Items, Outlets, Users, etc.).\n\n" +
                "A timestamped backup of the SQLite DB will be created.\n\nProceed?",
                "Reset Stock Data");

            if (!ok1) return;

            var ok2 = await dialogs.ConfirmAsync(
                "Final confirmation: Are you absolutely sure?\n\nTip: Close running sales/purchases windows first.",
                "Confirm Reset");

            if (!ok2) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await reset.RunAsync();
                Mouse.OverrideCursor = null;

                await dialogs.AlertAsync(
                    "Stock data has been reset.\n\n" +
                    "A backup was created under %LocalAppData%\\PosSuite\\backups.\n" +
                    "You may restart the app if any lists still show stale rows.",
                    "Done");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                await dialogs.AlertAsync("Reset failed:\n\n" + ex.Message, "Error");
            }
        }
   


    }
}
