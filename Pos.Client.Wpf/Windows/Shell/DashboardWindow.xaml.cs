using System.Windows.Input;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Sales;
using System.Windows.Media;
using System.Windows.Controls;
using Pos.Client.Wpf.Debugging;


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
            FocusTracer.Attach(this, "SHELL");
            #endif


            _vm = vm;
            _views = views;
            DataContext = _vm;

            Loaded += (_, __) =>
            {
                _views.Attach(_vm);
                // Optional: land on a default view (e.g., Reports or a Home view)
                // _views.SetRoot<Windows.Reports.ReportsView>();
                _ = _vm.RefreshAsync();
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape && _vm.IsOverlayOpen)
                {
                    _views.HideOverlay();
                    e.Handled = true;
                }
            };
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
        //private void OverlayLayer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        //{
        //    if (OverlayLayer.IsVisible)
        //    {
        //        _focusBeforeOverlay = Keyboard.FocusedElement;

        //        Dispatcher.BeginInvoke(new Action(() =>
        //        {
        //            var first = FindFirstFocusable(OverlayLayer);
        //            if (first != null) Keyboard.Focus(first);
        //        }), System.Windows.Threading.DispatcherPriority.Input);
        //    }
        //    else
        //    {
        //        // block Ribbon focus briefly; let tab reclaim it
        //        _suppressRibbonFocusUntilUtc = DateTime.UtcNow.AddMilliseconds(220);

        //        void FocusActiveScan()
        //        {
        //            if (DocumentTabs?.SelectedContent is IScanFocusable scanTarget)
        //            {
        //                if (DocumentTabs.SelectedContent is FrameworkElement fe)
        //                {
        //                    var scope = FocusManager.GetFocusScope(DocumentTabs);
        //                    FocusManager.SetFocusedElement(scope, fe);
        //                    DocumentTabs.Focus();
        //                    fe.Focus();
        //                }
        //                scanTarget.FocusScan();
        //            }
        //        }

        //        Dispatcher.BeginInvoke(new Action(() =>
        //        {
        //            if (TryRestorePreviousFocus(_focusBeforeOverlay)) return;

        //            FocusActiveScan();
        //            // re-assert once more to beat any late Ribbon/keytip grabs
        //            Dispatcher.BeginInvoke(new Action(FocusActiveScan),
        //                System.Windows.Threading.DispatcherPriority.Background);
        //        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        //        _focusBeforeOverlay = null;
        //    }
        //}



        //private static bool TryRestorePreviousFocus(IInputElement? prev)
        //{
        //    if (prev is FrameworkElement fe &&
        //        fe.IsVisible && fe.IsEnabled && fe.IsLoaded)
        //    {
        //        Keyboard.Focus(fe);
        //        fe.Focus();
        //        return true;
        //    }
        //    if (prev is FrameworkContentElement fce && fce.IsEnabled)
        //    {
        //        Keyboard.Focus(fce);
        //        return true;
        //    }
        //    return false;
        //}

        //private static IInputElement? FindFirstFocusable(DependencyObject root)
        //{
        //    for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        //    {
        //        var child = VisualTreeHelper.GetChild(root, i);

        //        // UIElement branch (most controls)
        //        if (child is UIElement uie && uie.IsVisible && uie.IsEnabled && uie.Focusable)
        //            return uie;

        //        // ContentElement branch (rare, but supported)
        //        if (child is ContentElement ce && ce.IsEnabled && ce.Focusable)
        //            return ce;

        //        var sub = FindFirstFocusable(child);
        //        if (sub != null) return sub;
        //    }
        //    return null;
        //}


        //public void FocusActiveTabAndScan()
        //{
        //    Activate(); // keep your re-activate

        //    if (DocumentTabs?.SelectedContent is IScanFocusable scanTarget)
        //    {
        //        // Give tab content logical focus first (prevents header grabbing it)
        //        if (DocumentTabs.SelectedContent is FrameworkElement fe)
        //        {
        //            DocumentTabs.Focus();
        //            fe.Focus();
        //            var scope = FocusManager.GetFocusScope(fe);
        //            FocusManager.SetFocusedElement(scope, fe);
        //        }

        //        // Now delegate to the view that knows how to focus its scan box
        //        scanTarget.FocusScan();
        //    }
        //    // else: do nothing — no rummaging through other tabs/controls
        //}

        //private DateTime _suppressRibbonFocusUntilUtc = DateTime.MinValue;

        //private void Ribbon_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        //{
        //    // If we’re in the suppression window, stop Ribbon from taking focus
        //    if (DateTime.UtcNow <= _suppressRibbonFocusUntilUtc)
        //    {
        //        e.Handled = true;
        //        // re-assert focus to active tab content + scan box
        //        FocusActiveTabAndScan();
        //    }
        //}



    }
}
