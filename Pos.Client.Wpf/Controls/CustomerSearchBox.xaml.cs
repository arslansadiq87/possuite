using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Services;              // IPartyLookupService, ITerminalContext
using Pos.Domain.Entities;              // RoleType
// ============================================================================
// CONTROL: CustomerSearchBox (Universal Party Search)
// ----------------------------------------------------------------------------
// PURPOSE:
//     A reusable auto-suggest search box for selecting customers, suppliers,
//     or both, from the Party entity. Designed for fast, outlet-aware lookups
//     using IPartyLookupService with offline/online sync support.
//
// USAGE:
//     <controls:CustomerSearchBox />                     → Customers only (default)
//     <controls:CustomerSearchBox Mode="Suppliers" />    → Suppliers only
//     <controls:CustomerSearchBox Mode="Both" />         → Both customers & suppliers
//
// BINDABLE PROPERTIES:
//     • Query                → string (two-way)
//     • SelectedCustomer     → CustomerLookupRow (two-way)
//     • SelectedCustomerId   → int? (two-way)
//     • Mode                 → PartyLookupMode { Customers | Suppliers | Both }
//     • WatermarkText        → string (auto-sets based on Mode; can override)
//
// EVENTS:
//     • CustomerPicked       → Raised when a suggestion is selected.
//
// DEPENDENCIES:
//     • IPartyLookupService  → provided via DI, implemented in Persistence layer
//     • ITerminalContext     → provides OutletId for outlet-level filtering
//
// NOTES:
//     • Debounced 250 ms for performance.
//     • Requires at least 2 characters to search.
//     • Auto-updates watermark based on Mode.
//     • Displays Name + Phone in suggestion list.
//     • Safe to use across Sales, Purchases, Returns, and shared Party forms.
//
// AUTHOR: POS Suite Team
// ============================================================================

namespace Pos.Client.Wpf.Controls
{
    public partial class CustomerSearchBox : UserControl
    {
        public enum PartyLookupMode { Customers, Suppliers, Both }

        public ObservableCollection<CustomerLookupRow> Suggestions { get; } = new();

        private readonly DispatcherTimer _debounce;
        private CancellationTokenSource? _cts;

        public CustomerSearchBox()
        {
            InitializeComponent();

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += async (_, __) => await RunSearchAsync();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        #region Bindable props

        public static readonly DependencyProperty SelectedCustomerProperty =
            DependencyProperty.Register(nameof(SelectedCustomer), typeof(CustomerLookupRow),
                typeof(CustomerSearchBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public CustomerLookupRow? SelectedCustomer
        {
            get => (CustomerLookupRow?)GetValue(SelectedCustomerProperty);
            set => SetValue(SelectedCustomerProperty, value);
        }

        public static readonly DependencyProperty SelectedCustomerIdProperty =
            DependencyProperty.Register(nameof(SelectedCustomerId), typeof(int?),
                typeof(CustomerSearchBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int? SelectedCustomerId
        {
            get => (int?)GetValue(SelectedCustomerIdProperty);
            set => SetValue(SelectedCustomerIdProperty, value);
        }

        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.Register(nameof(Query), typeof(string),
                typeof(CustomerSearchBox), new PropertyMetadata(string.Empty, OnQueryChanged));

        public string Query
        {
            get => (string)GetValue(QueryProperty);
            set => SetValue(QueryProperty, value);
        }

        public static readonly RoutedEvent CustomerPickedEvent =
            EventManager.RegisterRoutedEvent(nameof(CustomerPicked), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(CustomerSearchBox));

        public event RoutedEventHandler CustomerPicked
        {
            add => AddHandler(CustomerPickedEvent, value);
            remove => RemoveHandler(CustomerPickedEvent, value);
        }

        // ===== NEW: Mode =====
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(PartyLookupMode),
                typeof(CustomerSearchBox),
                new PropertyMetadata(PartyLookupMode.Customers, OnModeChanged));

        public PartyLookupMode Mode
        {
            get => (PartyLookupMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        // ===== NEW: WatermarkText (auto-updates with Mode but can be overridden) =====
        public static readonly DependencyProperty WatermarkTextProperty =
            DependencyProperty.Register(nameof(WatermarkText), typeof(string),
                typeof(CustomerSearchBox),
                new PropertyMetadata("Type customer's name or phone"));

        public string WatermarkText
        {
            get => (string)GetValue(WatermarkTextProperty);
            set => SetValue(WatermarkTextProperty, value);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (CustomerSearchBox)d;
            self.WatermarkText = self.Mode switch
            {
                PartyLookupMode.Customers => "Type customer's name or phone",
                PartyLookupMode.Suppliers => "Type supplier's name or phone",
                PartyLookupMode.Both => "Type name or phone (customer/supplier)",
                _ => "Type name or phone"
            };

            self._debounce.Stop();
            self._debounce.Start();
        }

        #endregion

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // wired in XAML: TextChanged + PreviewKeyDown
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _debounce.Stop();
            CancelPendingSearch();
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // keep Query in sync and (debounced) refresh suggestions
            Query = QueryBox.Text ?? string.Empty;
            _debounce.Stop();
            _debounce.Start();

            // open popup immediately if we already have suggestions; otherwise it will open after search
            var has = Suggestions.Count > 0;
            SetPopup(has && Query.Length >= 2);
            if (SuggestPopup.IsOpen && SuggestList.Items.Count > 0 && SuggestList.SelectedIndex < 0)
                SuggestList.SelectedIndex = 0;
        }

        private void QueryBox_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Enter: pick selected row (or first)
                if (SuggestList.SelectedItem is CustomerLookupRow row) { Pick(row); }
                else if (SuggestList.Items.Count > 0) { SuggestList.SelectedIndex = 0; if (SuggestList.SelectedItem is CustomerLookupRow first) Pick(first); }
                e.Handled = true; return;
            }

            if (e.Key == Key.Down)
            {
                // Down: open and move focus to list
                if (!SuggestPopup.IsOpen && Suggestions.Count > 0) SetPopup(true);
                if (SuggestList.Items.Count > 0 && SuggestList.SelectedIndex < 0) SuggestList.SelectedIndex = 0;
                SuggestList.Focus();
                e.Handled = true; return;
            }

            if (e.Key == Key.Escape && SuggestPopup.IsOpen)
            {
                SetPopup(false);
                e.Handled = true; return;
            }
            // NOTE: Backspace and normal typing fall through (not handled) → TextChanged will run
        }

        private void SuggestList_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SuggestList.SelectedItem is CustomerLookupRow row) Pick(row);
                e.Handled = true; return;
            }
            if (e.Key == Key.Escape)
            {
                SetPopup(false);
                QueryBox.Focus();
                e.Handled = true; return;
            }
            if (e.Key == Key.Up && SuggestList.SelectedIndex == 0)
            {
                // hand control back to textbox
                QueryBox.Focus();
                QueryBox.CaretIndex = Query?.Length ?? 0;
                e.Handled = true; return;
            }
        }


        private static void OnQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (CustomerSearchBox)d;
            self._debounce.Stop();
            self._debounce.Start();
        }

        private void CancelPendingSearch()
        {
            try
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                cts?.Cancel();
                cts?.Dispose();
            }
            catch
            {
                // swallow — UI control should not throw on teardown
            }
        }

        private async Task RunSearchAsync()
        {
            _debounce.Stop();
            CancelPendingSearch();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var q = (Query ?? string.Empty).Trim();
            if (q.Length < 2)
            {
                Suggestions.Clear();
                SetPopup(false);
                return;
            }

            try
            {
                var svc = App.Services.GetRequiredService<IPartyLookupService>();
                var termCtx = App.Services.GetRequiredService<ITerminalContext>();
                var outletId = termCtx.OutletId;

                RoleType? role = Mode switch
                {
                    PartyLookupMode.Customers => RoleType.Customer,
                    PartyLookupMode.Suppliers => RoleType.Supplier,
                    _ => (RoleType?)null
                };

                var parties = await svc.SearchPartiesAsync(q, role, outletId, 20, ct);

                Suggestions.Clear();
                foreach (var p in parties)
                {
                    Suggestions.Add(new CustomerLookupRow
                    {
                        Id = p.Id,
                        Name = p.Name ?? string.Empty,
                        Phone = p.Phone ?? string.Empty,
                        Code = string.Empty
                    });
                }

                OpenAndFocusIfAny();
            }
            catch (OperationCanceledException)
            {
                Suggestions.Clear();
                SetPopup(false);
            }
            catch
            {
                Suggestions.Clear();
                SetPopup(false);
            }
        }

        private void Pick(CustomerLookupRow? row)
        {
            SelectedCustomer = row;
            SelectedCustomerId = row?.Id;
            Query = row?.ToString() ?? string.Empty;
            SetPopup(false);
            RaiseEvent(new RoutedEventArgs(CustomerPickedEvent));
        }

        private void SuggestList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SuggestList.SelectedItem is CustomerLookupRow row) Pick(row);
        }

        

        private void FocusFirstItem()
        {
            if (SuggestList.Items.Count > 0)
            {
                SuggestList.SelectedIndex = 0;
                if (SuggestList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
                    item.Focus();
            }
        }

        private void OpenAndFocusIfAny()
        {
            SetPopup(Suggestions.Any());
            if (SuggestPopup.IsOpen) FocusFirstItem();
        }


        private void SetPopup(bool open)
        {
            if (SuggestPopup != null)
                SuggestPopup.IsOpen = open;
        }

        // UI-only projection for suggestion list
        public sealed class CustomerLookupRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public override string ToString() =>
                string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name}  ({Phone})";
        }
    }
}
