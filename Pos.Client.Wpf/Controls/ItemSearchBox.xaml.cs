using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.DTO;
using Pos.Domain.Services;
using Pos.Domain.Entities;      // <-- for InventoryLocationType

using System.Threading.Tasks;

namespace Pos.Client.Wpf.Controls
{ 
    public partial class ItemSearchBox : UserControl
    {
        // Routed event: fires when user confirms a pick (Enter/dbl-click)
        public static readonly RoutedEvent ItemPickedEvent =
            EventManager.RegisterRoutedEvent(nameof(ItemPicked), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(ItemSearchBox));
        public event RoutedEventHandler ItemPicked { add => AddHandler(ItemPickedEvent, value); remove => RemoveHandler(ItemPickedEvent, value); }
        // The selected item (read-only bindable)
        public ItemIndexDto? SelectedItem
        {
            get => (ItemIndexDto?)GetValue(SelectedItemProperty);
            private set => SetValue(SelectedItemPropertyKey, value);
        }
        private static readonly DependencyPropertyKey SelectedItemPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(SelectedItem), typeof(ItemIndexDto), typeof(ItemSearchBox), new PropertyMetadata(null));
        public static readonly DependencyProperty SelectedItemProperty = SelectedItemPropertyKey.DependencyProperty;
        // Optional: expose the raw query text
        public string Query
        {
            get => (string)GetValue(QueryProperty);
            set => SetValue(QueryProperty, value);
        }
        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.Register(nameof(Query), typeof(string), typeof(ItemSearchBox),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // If not set, we fall back to ITerminalContext.OutletId (Outlet)
        public InventoryLocationType? LocationType
        {
            get => (InventoryLocationType?)GetValue(LocationTypeProperty);
            set => SetValue(LocationTypeProperty, value);
        }

        public static readonly DependencyProperty LocationTypeProperty =
            DependencyProperty.Register(
                nameof(LocationType),
                typeof(InventoryLocationType?),
                typeof(ItemSearchBox),
                new PropertyMetadata(null, OnScopeChanged));

        public int? LocationId
        {
            get => (int?)GetValue(LocationIdProperty);
            set => SetValue(LocationIdProperty, value);
        }

        public static readonly DependencyProperty LocationIdProperty =
            DependencyProperty.Register(
                nameof(LocationId),
                typeof(int?),
                typeof(ItemSearchBox),
                new PropertyMetadata(null, OnScopeChanged));

        // ----- Services & state -----
        private IItemsReadService? _lookup;
        private IInventoryReadService? _invRead;
        private ITerminalContext? _terminal;

        private readonly ObservableCollection<ItemIndexDto> _index = new();
        private ICollectionView? _view;

        // Scanner-burst handling
        private DateTime _lastAt = DateTime.MinValue;
        private int _burstCount = 0;
        private bool _suppressDropdown = false;
        private readonly DispatcherTimer _burstReset =
            new() { Interval = TimeSpan.FromMilliseconds(220) };

        public ItemSearchBox()
        {
            InitializeComponent();

            // 🔹 Don’t touch DI / services in the designer
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            var sp = App.Services;
            if (sp is null)
                return; // still not ready – fail silently (designer / early runtime)

            _lookup = sp.GetRequiredService<IItemsReadService>();
            _invRead = sp.GetService<IInventoryReadService>();
            _terminal = sp.GetService<ITerminalContext>();


            Loaded += OnLoaded;
            _burstReset.Tick += (_, __) =>
            {
                _suppressDropdown = false;
                _burstCount = 0;
                _burstReset.Stop();
            };
        }


        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this)) return;
            if (_lookup is null) return;

            var list = await _lookup.BuildIndexAsync();

            _index.Clear();
            foreach (var it in list)
                _index.Add(it);

            // Load on-hand stock for current scope
            await RefreshOnHandAsync();

            _view = CollectionViewSource.GetDefaultView(_index);
            _view.Filter = o =>
            {
                if (o is not ItemIndexDto i) return false;
                var term = (SearchBox.Text ?? "").Trim();
                if (term.Length == 0) return true;

                return (i.DisplayName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (i.Sku?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (i.Barcode?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            };

            List.ItemsSource = _view;
        }

        private async Task RefreshOnHandAsync()
        {
            if (!IsLoaded) return;
            if (_invRead is null) return;
            if (_index.Count == 0) return;

            // Resolve scope: explicit (LocationType/LocationId) or fallback to current outlet
            InventoryLocationType? locType = LocationType;
            int? locId = LocationId;

            if (!locType.HasValue || !locId.HasValue || locId.GetValueOrDefault() <= 0)
            {
                if (_terminal is null || _terminal.OutletId <= 0)
                    return;

                locType = InventoryLocationType.Outlet;
                locId = _terminal.OutletId;
            }

            var ids = _index.Select(x => x.Id).Distinct().ToArray();
            if (ids.Length == 0) return;

            Dictionary<int, decimal> map;
            try
            {
                // atUtc: null => uses DateTime.UtcNow internally
                map = await _invRead.GetOnHandBulkAsync(
                    ids,
                    locType.Value,
                    locId.Value,
                    atUtc: null,
                    ct: CancellationToken.None);
            }
            catch
            {
                // fail silently; leave old values if any
                return;
            }

            // rewrite records with updated OnHand (record 'with' syntax)
            for (int i = 0; i < _index.Count; i++)
            {
                var row = _index[i];
                if (!map.TryGetValue(row.Id, out var qty))
                    qty = 0m;

                _index[i] = row with { OnHand = qty };
            }

            _view?.Refresh();
        }

        private static async void OnScopeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ItemSearchBox box) return;

            try
            {
                await box.RefreshOnHandAsync();
            }
            catch
            {
                // ignore UI-level failures
            }
        }

        public void FocusSearch()
        {
            Keyboard.Focus(SearchBox);
            SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
        }

        public void Clear()
        {
            Query = "";
            SearchBox.Text = "";
            Popup.IsOpen = false;
            _suppressDropdown = false;
            _burstCount = 0;
            FocusSearch();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Query = SearchBox.Text ?? "";
            var now = DateTime.UtcNow;
            _burstCount = (now - _lastAt).TotalMilliseconds <= 40 ? _burstCount + 1 : 1;
            _lastAt = now;
            if (_burstCount >= 4) { _suppressDropdown = true; _burstReset.Stop(); _burstReset.Start(); }
            _view?.Refresh();
            var has = _view != null && _view.Cast<object>().Any();
            Popup.IsOpen = !_suppressDropdown && Query.Length > 0 && has;
            if (Popup.IsOpen && List.Items.Count > 0 && List.SelectedIndex < 0)
                List.SelectedIndex = 0;
        }

        private async Task ConfirmPickAsync()
        {
            ItemIndexDto? pick = null;
            if (Popup.IsOpen && List.SelectedItem is ItemIndexDto sel) pick = sel;
            if (pick is null && !string.IsNullOrWhiteSpace(Query))
                pick = await _lookup.FindOneAsync(Query.Trim());
            if (pick is null) return;
            SelectedItem = pick;
            RaiseEvent(new RoutedEventArgs(ItemPickedEvent, this));
            Clear();
        }

        private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ConfirmPickAsync(); return; }
            if (e.Key == Key.Down)
            {
                if (!Popup.IsOpen)
                {
                    _view?.Refresh();
                    if (_view != null && _view.Cast<object>().Any())
                        Popup.IsOpen = true;
                }
                if (Popup.IsOpen && List.Items.Count > 0)
                {
                    if (List.SelectedIndex < 0) List.SelectedIndex = 0;
                    List.Focus();
                }
                e.Handled = true; return;
            }
            if (e.Key == Key.Escape && Popup.IsOpen)
            {
                Popup.IsOpen = false; e.Handled = true; return;
            }
        }

        private async void List_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; await ConfirmPickAsync(); return; }
            if (e.Key == Key.Escape) { Popup.IsOpen = false; SearchBox.Focus(); e.Handled = true; }
            if (e.Key == Key.Up && List.SelectedIndex == 0)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
                e.Handled = true;
            }
        }

        private async void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => await ConfirmPickAsync();
    }
}