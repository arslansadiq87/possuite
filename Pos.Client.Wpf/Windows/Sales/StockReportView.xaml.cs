//Pos.Client.Wpf/StockReportWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Domain.Models.Reports;
using Pos.Client.Wpf.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Security;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class StockReportView : UserControl, IRefreshOnActivate
    {
        private readonly IReportsService _reports;
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private enum LocationScope { Outlet, Warehouse }     // which dimension is active
        private LocationScope _scope = LocationScope.Outlet;
        private const int AllId = -1;                        // sentinel for "All ..."
        private bool _suppressScopeEvents = false;  // don't react while we set up UI
        private bool _scopeUiReady = false;         // only reload after first init

        

        private ViewMode _mode = ViewMode.ByItem;
        private List<ItemRow> _itemRows = new();
        private List<ProductRow> _productRows = new();

        private enum ViewMode { ByItem, ByProduct }

        private sealed class ItemRow
        {
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";   // Product + variant
            public string Variant { get; set; } = "";
            public string Brand { get; set; } = "";
            public string Category { get; set; } = "";
            public decimal OnHand { get; set; }
        }

        private sealed class ProductRow
        {
            public string Product { get; set; } = "";
            public string Brand { get; set; } = "";
            public string Category { get; set; } = "";
            public decimal OnHand { get; set; }
        }

        public StockReportView()
        {
            InitializeComponent();
            _reports = App.Services.GetRequiredService<IReportsService>();

            ModeByItemBtn.IsChecked = true;     // default mode
            ScopeOutletBtn.IsChecked = true;    // default scope

            ScopePanel.Visibility = AuthZ.IsAdminCached() ? Visibility.Visible : Visibility.Collapsed;

            OutletBox.DisplayMemberPath = "Name";
            OutletBox.SelectedValuePath = "Id";
            WarehouseBox.DisplayMemberPath = "Name";
            WarehouseBox.SelectedValuePath = "Id";
        
            var _ = Dispatcher.InvokeAsync(async () =>
            {
                _suppressScopeEvents = true;

                await InitScopeAsync();

                _suppressScopeEvents = false;
                _scopeUiReady = true;

                LoadDataByItemWithVariants();
            });
        }


        private async Task InitScopeAsync()
        {
            var isAdmin = AuthZ.IsAdminCached();
            var uid = AppState.Current.CurrentUserId;

            // Outlets
            var outlets = await _reports.GetOutletsForUserAsync(uid, isAdmin);
            if (isAdmin)
            {
                OutletBox.ItemsSource =
                    new[] { new Outlet { Id = AllId, Name = "All Outlets" } }
                    .Concat(outlets)
                    .ToList();
                OutletBox.SelectedIndex = 0;
                ScopeOutletBtn.IsChecked = true;
                ScopeOutletBtn.IsEnabled = OutletBox.Items.Count > 0;
                OutletBox.IsEnabled = OutletBox.Items.Count > 0;
                ScopeWarehouseBtn.IsEnabled = true;
                WarehouseBox.IsEnabled = false;
            }
            else
            {
                OutletBox.ItemsSource = outlets;
                OutletBox.SelectedItem =
                outlets.FirstOrDefault(o => o.Id == AppState.Current.CurrentOutletId) ?? outlets.FirstOrDefault();
    
                ScopeOutletBtn.IsChecked = true;
                ScopeWarehouseBtn.IsEnabled = false;
                WarehouseBox.IsEnabled = false;
    
                if (outlets.Count <= 1)
                    {
                    ScopeOutletBtn.IsEnabled = false;
                    OutletBox.IsEnabled = false;
                    }
                }

       
            if (isAdmin)
                {
                var warehouses = await _reports.GetWarehousesAsync();
                WarehouseBox.ItemsSource =
                new[] { new Warehouse { Id = AllId, Name = "All Warehouses" } }
                        .Concat(warehouses)
                        .ToList();
                WarehouseBox.SelectedIndex = 0;
                }
            }
   

        private void ApplyScopeEnablement()
        {
            bool isOutlet = _scope == LocationScope.Outlet;

            if (OutletBox != null)
                OutletBox.IsEnabled = isOutlet && OutletBox.Items.Count > 0;

            if (WarehouseBox != null)
                WarehouseBox.IsEnabled = !isOutlet && WarehouseBox.Items.Count > 0;
        }

        private void OutletBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressScopeEvents || !_scopeUiReady) return;
            if (_scope != LocationScope.Outlet) return;
            ReloadCurrentView();
        }

        private void WarehouseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressScopeEvents || !_scopeUiReady) return;
            if (_scope != LocationScope.Warehouse) return;
            ReloadCurrentView();
        }


        private void ReloadCurrentView()
        {
            if (_mode == ViewMode.ByItem) LoadDataByItemWithVariants();
            else LoadDataByProduct();
        }

        // ===== Toolbar buttons =====
        private void ByItem_Click(object sender, RoutedEventArgs e)
        {
            _mode = ViewMode.ByItem;
            LoadDataByItemWithVariants();
            SearchBox.Focus();
        }

        private void ByProduct_Click(object sender, RoutedEventArgs e)
        {
            _mode = ViewMode.ByProduct;
            LoadDataByProduct();
            SearchBox.Focus();
        }

        // ===== Column builders =====
        private void ConfigureColumnsForItem()
        {
            Grid.Columns.Clear();
            Grid.Columns.Add(new DataGridTextColumn { Header = "SKU", Binding = new Binding("Sku"), Width = 140 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "Display Name", Binding = new Binding("DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            // NEW: Brand & Category columns
            Grid.Columns.Add(new DataGridTextColumn { Header = "Brand", Binding = new Binding("Brand"), Width = 150 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("Category"), Width = 170 });

            Grid.Columns.Add(new DataGridTextColumn { Header = "Variant", Binding = new Binding("Variant"), Width = 220 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "On Hand", Binding = new Binding("OnHand") { StringFormat = "N2" }, Width = 90 });
        }

        private void ConfigureColumnsForProduct()
        {
            Grid.Columns.Clear();
            Grid.Columns.Add(new DataGridTextColumn { Header = "Product", Binding = new Binding("Product"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.Columns.Add(new DataGridTextColumn { Header = "Brand", Binding = new Binding("Brand"), Width = 150 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new Binding("Category"), Width = 170 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "On Hand", Binding = new Binding("OnHand"), Width = 120 });
        }

       

        // ====
        private async void LoadDataByItemWithVariants()
        {
            // Map current scope → service inputs
            InventoryLocationType scopeType = _scope == LocationScope.Outlet
                            ? InventoryLocationType.Outlet
                            : InventoryLocationType.Warehouse;
            int ? scopeId = null;
                        if (_scope == LocationScope.Outlet)
                            {
                var sel = OutletBox.SelectedItem as Outlet;
                scopeId = (sel == null || sel.Id == AllId) ? (int?)null : sel.Id;
                            }
                        else
                            {
                var sel = WarehouseBox.SelectedItem as Warehouse;
                scopeId = (sel == null || sel.Id == AllId) ? (int?)null : sel.Id;
                            }
            
            var list = await _reports.StockOnHandByItemAsync(scopeType, scopeId);
            
            var raw = list.Select(x => new ItemRow
                        {
                Sku = x.Sku,
                    DisplayName = x.DisplayName,
                    Brand = x.Brand,
                    Category = x.Category,
                    Variant = x.Variant,
                    OnHand = x.OnHand
            }).ToList();

            _itemRows = raw;
            ConfigureColumnsForItem();
            ApplySearchFilter();
        }


        private async void LoadDataByProduct()
        {
      
            InventoryLocationType scopeType = _scope == LocationScope.Outlet
                ? InventoryLocationType.Outlet
                : InventoryLocationType.Warehouse;
            int ? scopeId = null;
                        if (_scope == LocationScope.Outlet)
                            {
                var sel = OutletBox.SelectedItem as Outlet;
                scopeId = (sel == null || sel.Id == AllId) ? (int?)null : sel.Id;
                            }
                        else
                            {
                var sel = WarehouseBox.SelectedItem as Warehouse;
                scopeId = (sel == null || sel.Id == AllId) ? (int?)null : sel.Id;
                            }
            
            var rows = (await _reports.StockOnHandByProductAsync(scopeType, scopeId))
                            .Select(x => new ProductRow
                            {
                Product = x.Product,
                    Brand = x.Brand,
                    Category = x.Category,
                    OnHand = x.OnHand
                })
                .ToList();

            _productRows = rows;

            ConfigureColumnsForProduct();
            ApplySearchFilter();
        }


        
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();

        private void ApplySearchFilter()
        {
            var term = (SearchBox.Text ?? "").Trim();
            if (_mode == ViewMode.ByItem)
            {
                IEnumerable<ItemRow> rows = _itemRows;
                if (term.Length > 0)
                    rows = rows.Where(r => ContainsIC(r.DisplayName, term) || ContainsIC(r.Sku, term));

                Grid.ItemsSource = rows.ToList();
            }
            else
            {
                IEnumerable<ProductRow> rows = _productRows;
                if (term.Length > 0)
                    rows = rows.Where(r =>
                        ContainsIC(r.Product, term) ||
                        ContainsIC(r.Brand, term) ||
                        ContainsIC(r.Category, term));

                Grid.ItemsSource = rows.ToList();
            }


            SelectFirstRow();
        }

        private static bool ContainsIC(string? hay, string needle)
            => !string.IsNullOrEmpty(hay) &&
               hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private void SelectFirstRow()
        {
            if (Grid.Items.Count == 0) return;
            Grid.SelectedIndex = 0;
            Grid.ScrollIntoView(Grid.SelectedItem);
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down) { MoveSelection(+1); e.Handled = true; }
            else if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
            else if (e.Key == Key.Down) { MoveSelection(+1); e.Handled = true; }
        }

        private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                return; // let access keys go through

            if (!string.IsNullOrEmpty(e.Text) && e.Text.Any(char.IsLetterOrDigit))
            {
                if (!SearchBox.IsKeyboardFocused) SearchBox.Focus();
                var caretAtEnd = SearchBox.CaretIndex >= (SearchBox.Text?.Length ?? 0);
                SearchBox.Text += e.Text;
                if (caretAtEnd) SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
            }
        }


        private void MoveSelection(int delta)
        {
            if (Grid.Items.Count == 0) return;
            if (!Grid.IsKeyboardFocusWithin) Grid.Focus();

            var idx = Grid.SelectedIndex;
            if (idx < 0) idx = 0;

            idx = Math.Clamp(idx + delta, 0, Grid.Items.Count - 1);
            Grid.SelectedIndex = idx;
            Grid.ScrollIntoView(Grid.SelectedItem);
        }

        // ===== Mode toggles =====
        private void ModeByItem_Checked(object sender, RoutedEventArgs e)
        {
            // keep group exclusive
            if (ModeByProductBtn.IsChecked == true) ModeByProductBtn.IsChecked = false;

            if (_mode != ViewMode.ByItem)
            {
                _mode = ViewMode.ByItem;
                LoadDataByItemWithVariants();
            }
            SearchBox.Focus();
        }
        private void ModeByItem_Unchecked(object sender, RoutedEventArgs e)
        {
            // prevent both off: if user unchecks manually, re-check unless other got checked
            if (ModeByProductBtn.IsChecked != true)
                ModeByItemBtn.IsChecked = true;
        }

        private void ModeByProduct_Checked(object sender, RoutedEventArgs e)
        {
            if (ModeByItemBtn.IsChecked == true) ModeByItemBtn.IsChecked = false;

            if (_mode != ViewMode.ByProduct)
            {
                _mode = ViewMode.ByProduct;
                LoadDataByProduct();
            }
            SearchBox.Focus();
        }
        private void ModeByProduct_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ModeByItemBtn.IsChecked != true)
                ModeByProductBtn.IsChecked = true;
        }

        // ===== Scope toggles =====
        private void ScopeOutlet_Checked(object sender, RoutedEventArgs e)
        {
            if (ScopeWarehouseBtn.IsChecked == true) ScopeWarehouseBtn.IsChecked = false;

            _scope = LocationScope.Outlet;
            ApplyScopeEnablement();
            if (_scopeUiReady) ReloadCurrentView();
        }
        private void ScopeOutlet_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ScopeWarehouseBtn.IsChecked != true)
                ScopeOutletBtn.IsChecked = true;
        }

        private void ScopeWarehouse_Checked(object sender, RoutedEventArgs e)
        {
            if (ScopeOutletBtn.IsChecked == true) ScopeOutletBtn.IsChecked = false;

            _scope = LocationScope.Warehouse;
            ApplyScopeEnablement();
            if (_scopeUiReady) ReloadCurrentView();
        }
        private void ScopeWarehouse_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ScopeOutletBtn.IsChecked != true)
                ScopeWarehouseBtn.IsChecked = true;
        }

        public void OnActivated()
        {
            // tiny throttle so activation + selection doesn't double-call
            var now = DateTime.UtcNow;
            if (now - _lastRefreshUtc < TimeSpan.FromMilliseconds(250)) return;
            _lastRefreshUtc = now;

            // If scope UI hasn't finished its first async init, defer slightly
            if (!_scopeUiReady)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_scopeUiReady) ReloadCurrentView();
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // Refresh according to the current mode + current scope & pickers
            if (!IsLoaded)
                Dispatcher.BeginInvoke(new Action(ReloadCurrentView));
            else
                ReloadCurrentView();
        }

    }
}
