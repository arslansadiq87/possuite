//Pos.Client.Wpf/StockReportWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class StockReportView : UserControl
    {
        private enum LocationScope { Outlet, Warehouse }     // which dimension is active
        private LocationScope _scope = LocationScope.Outlet;
        private const int AllId = -1;                        // sentinel for "All ..."
        private bool _suppressScopeEvents = false;  // don't react while we set up UI
        private bool _scopeUiReady = false;         // only reload after first init

        private bool IsAdmin()
        {
            var u = AppState.Current?.CurrentUser;
            if (u != null && (u.Role == UserRole.Admin)) return true;

            var roles = (AppState.Current?.CurrentUserRole ?? "")
                        .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim());
            return roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                               || r.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
                               || r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
        }

        private readonly DbContextOptions<PosClientDbContext> _opts;
        //private const int OutletId = 1;

        private DateTime? _lastEscDown;
        private ViewMode _mode = ViewMode.ByItem;

        // in-memory caches so search filters instantly
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

            // Initial toggle states
            ModeByItemBtn.IsChecked = true;     // default mode
            ScopeOutletBtn.IsChecked = true;    // default scope

            // Admin-only visibility of the whole scope area
            ScopePanel.Visibility = IsAdmin() ? Visibility.Visible : Visibility.Collapsed;

            // keep these if you like:
            OutletBox.DisplayMemberPath = "Name";
            OutletBox.SelectedValuePath = "Id";
            WarehouseBox.DisplayMemberPath = "Name";
            WarehouseBox.SelectedValuePath = "Id";

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

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
            using var db = new PosClientDbContext(_opts);
            var isAdmin = IsAdmin();
            var uid = AppState.Current.CurrentUserId;

            // Outlets
            if (isAdmin)
            {
                var outlets = await db.Outlets.AsNoTracking().OrderBy(o => o.Name).ToListAsync();
                OutletBox.ItemsSource = new[] { new Outlet { Id = AllId, Name = "All Outlets" } }
                                        .Concat(outlets).ToList();
                OutletBox.SelectedIndex = 0;
            }
            else
            {
                var assignedIds = await db.Set<UserOutlet>().AsNoTracking()
                                          .Where(uo => uo.UserId == uid)
                                          .Select(uo => uo.OutletId)
                                          .ToListAsync();

                var outlets = await db.Outlets.AsNoTracking()
                                  .Where(o => assignedIds.Contains(o.Id))
                                  .OrderBy(o => o.Name)
                                  .ToListAsync();

                OutletBox.ItemsSource = outlets;
                OutletBox.SelectedItem =
                    outlets.FirstOrDefault(o => o.Id == AppState.Current.CurrentOutletId) ?? outlets.FirstOrDefault();

                // NEW (non-admin): force Outlet scope and lock controls as needed
                ScopeOutletBtn.IsChecked = true;          // make sure outlet is the active scope
                ScopeWarehouseBtn.IsEnabled = false;      // cannot switch to warehouse
                WarehouseBox.IsEnabled = false;           // cannot change warehouse

                if (outlets.Count <= 1)
                {
                    ScopeOutletBtn.IsEnabled = false;     // only one outlet, lock toggle
                    OutletBox.IsEnabled = false;          // and its picker
                }
            }

            // Warehouses
            if (isAdmin)
            {
                var warehouses = await db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync();
                WarehouseBox.ItemsSource = new[] { new Warehouse { Id = AllId, Name = "All Warehouses" } }
                                           .Concat(warehouses).ToList();
                WarehouseBox.SelectedIndex = 0;
            }
            else
            {
                // For non-admins we already hid/disabled via constructor visibility;
                // nothing else needed here.
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



        private IQueryable<StockEntry> BuildScopedLedger(PosClientDbContext db)
        {
            var q = db.StockEntries.AsNoTracking();

            if (_scope == LocationScope.Outlet)
            {
                var sel = OutletBox.SelectedItem as Outlet;
                if (sel == null) return q.Where(_ => false);
                if (sel.Id == AllId) return q.Where(s => s.LocationType == InventoryLocationType.Outlet);
                return q.Where(s => s.LocationType == InventoryLocationType.Outlet && s.LocationId == sel.Id);
            }
            else
            {
                var sel = WarehouseBox.SelectedItem as Warehouse;
                if (sel == null) return q.Where(_ => false);
                if (sel.Id == AllId) return q.Where(s => s.LocationType == InventoryLocationType.Warehouse);
                return q.Where(s => s.LocationType == InventoryLocationType.Warehouse && s.LocationId == sel.Id);
            }
        }


        // ===== Data loads (full lists into cache) =====
        private void LoadDataByItemWithVariants()
        {
            using var db = new PosClientDbContext(_opts);

            var ledger = BuildScopedLedger(db);

            var raw = (from i in db.Items.AsNoTracking()
                       join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                       from p in gp.DefaultIfEmpty()

                           // NEW: left joins for brand and category
                       join b in db.Set<Brand>().AsNoTracking() on p.BrandId equals b.Id into gb
                       from b in gb.DefaultIfEmpty()
                       join c in db.Set<Category>().AsNoTracking() on p.CategoryId equals c.Id into gc
                       from c in gc.DefaultIfEmpty()

                       join se in ledger on i.Id equals se.ItemId into gse
                       let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m
                       orderby (p != null ? p.Name : i.Name), i.Variant1Value, i.Variant2Value, i.Sku
                       select new
                       {
                           i.Sku,
                           ItemName = i.Name,
                           ProductName = p != null ? p.Name : null,
                           BrandName = b != null ? b.Name : null,
                           CategoryName = c != null ? c.Name : null,
                           i.Variant1Name,
                           i.Variant1Value,
                           i.Variant2Name,
                           i.Variant2Value,
                           OnHand = onHand
                       })
              .AsEnumerable()
              .Select(x => new ItemRow
              {
                  Sku = x.Sku,
                  DisplayName = BuildDisplayName(x.ProductName, x.ItemName, x.Variant1Name, x.Variant1Value, x.Variant2Name, x.Variant2Value),
                  Brand = x.BrandName ?? "",
                  Category = x.CategoryName ?? "",
                  Variant = BuildVariant(x.Variant1Name, x.Variant1Value, x.Variant2Name, x.Variant2Value),
                  OnHand = (int)Math.Round(x.OnHand, MidpointRounding.AwayFromZero)
              })
              .ToList();

            _itemRows = raw;
            ConfigureColumnsForItem();
            ApplySearchFilter();
        }


        private void LoadDataByProduct()
        {
            using var db = new PosClientDbContext(_opts);

            var ledger = BuildScopedLedger(db);

            var rows = (from i in db.Items.AsNoTracking()
                        join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                        from p in gp.DefaultIfEmpty()

                            // NEW: joins for brand and category
                        join b in db.Set<Brand>().AsNoTracking() on p.BrandId equals b.Id into gb
                        from b in gb.DefaultIfEmpty()
                        join c in db.Set<Category>().AsNoTracking() on p.CategoryId equals c.Id into gc
                        from c in gc.DefaultIfEmpty()

                        join se in ledger on i.Id equals se.ItemId into gse
                        let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m

                        group new { onHand, Brand = b != null ? b.Name : "", Category = c != null ? c.Name : "" }
                        by new
                        {
                            Prod = p != null ? p.Name : i.Name,
                            BrandName = b != null ? b.Name : "",
                            CategoryName = c != null ? c.Name : ""
                        }
                        into g
                        orderby g.Key.Prod
                        select new ProductRow
                        {
                            Product = g.Key.Prod,
                            Brand = g.Key.BrandName,
                            Category = g.Key.CategoryName,
                            OnHand = (int)Math.Round(g.Sum(x => x.onHand), MidpointRounding.AwayFromZero)
                        })
                       .ToList();

            _productRows = rows;

            ConfigureColumnsForProduct();
            ApplySearchFilter();
        }


        // ===== Search/filter (works in both modes) =====
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

        // ===== UX helpers =====
        private static string BuildVariant(string? n1, string? v1, string? n2, string? v2)
        {
            var p1 = string.IsNullOrWhiteSpace(v1) ? "" : $"{n1}: {v1}";
            var p2 = string.IsNullOrWhiteSpace(v2) ? "" : $"{(p1.Length > 0 ? "  " : "")}{n2}: {v2}";
            return (p1 + p2).Trim();
        }

        private static string BuildDisplayName(string? productName, string itemName,
                                               string? v1n, string? v1v, string? v2n, string? v2v)
        {
            var baseName = string.IsNullOrWhiteSpace(productName) ? itemName : productName;
            var v = BuildVariant(v1n, v1v, v2n, v2v);
            return string.IsNullOrWhiteSpace(v) ? baseName : $"{baseName} — {v}";
        }

        private void SelectFirstRow()
        {
            if (Grid.Items.Count == 0) return;
            Grid.SelectedIndex = 0;
            Grid.ScrollIntoView(Grid.SelectedItem);
        }

        // ===== Keyboard behavior =====

        // Up/Down while typing => move selection
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down) { MoveSelection(+1); e.Handled = true; }
            else if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
        }

        // Global: double-Esc to close; Up/Down selects; any alphanumeric focuses SearchBox
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; }
            else if (e.Key == Key.Down) { MoveSelection(+1); e.Handled = true; }
        }

        // This captures the actual character; we append it into SearchBox and focus it
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


    }
}
