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
        private IItemsReadService? _lookup;
        private readonly ObservableCollection<ItemIndexDto> _index = new();
        private ICollectionView? _view;
        // Scanner-burst handling
        private DateTime _lastAt = DateTime.MinValue;
        private int _burstCount = 0;
        private bool _suppressDropdown = false;
        private readonly DispatcherTimer _burstReset = new() { Interval = TimeSpan.FromMilliseconds(220) };

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
            foreach (var it in list) _index.Add(it);

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