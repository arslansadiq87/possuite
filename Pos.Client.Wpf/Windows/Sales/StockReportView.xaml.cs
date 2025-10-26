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
            public int OnHand { get; set; }
        }

        private sealed class ProductRow
        {
            public string Product { get; set; } = "";
            public int OnHand { get; set; }
        }

        public StockReportView()
        {
            InitializeComponent();

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

            // default view
            //LoadDataByItemWithVariants();
            var _ = Dispatcher.InvokeAsync(async () =>
            {
                _suppressScopeEvents = true;

                // show/hide the bar
                ScopeBar.Visibility = IsAdmin() ? Visibility.Visible : Visibility.Collapsed;

                await InitScopeAsync();   // populate OutletBox / WarehouseBox
                _suppressScopeEvents = false;
                _scopeUiReady = true;

                LoadDataByItemWithVariants();
            });


        }

        private async Task InitScopeAsync()
        {
            using var db = new PosClientDbContext(_opts);

            var isAdmin = IsAdmin();
            var myUserId = AppState.Current.CurrentUserId;

            // ----- Outlets list -----
            List<Outlet> outlets;
            if (isAdmin)
            {
                outlets = await db.Outlets.AsNoTracking()
                             .OrderBy(o => o.Name)
                             .ToListAsync();
                // Insert ALL
                OutletBox.ItemsSource = new[] { new Outlet { Id = AllId, Name = "All Outlets" } }
                                         .Concat(outlets)
                                         .ToList();
                OutletBox.SelectedIndex = 0; // default All
            }
            else
            {
                // Only assigned outlets
                var assignedIds = await db.Set<UserOutlet>()
                                          .AsNoTracking()
                                          .Where(uo => uo.UserId == myUserId)
                                          .Select(uo => uo.OutletId)
                                          .ToListAsync();

                outlets = await db.Outlets.AsNoTracking()
                             .Where(o => assignedIds.Contains(o.Id))
                             .OrderBy(o => o.Name)
                             .ToListAsync();

                OutletBox.ItemsSource = outlets;
                // Choose current outlet if present, else first
                var cur = outlets.FirstOrDefault(o => o.Id == AppState.Current.CurrentOutletId) ?? outlets.FirstOrDefault();
                OutletBox.SelectedItem = cur;

                // Lock the scope controls for non-admin
                ScopeWarehouseRadio.IsEnabled = false;
                WarehouseBox.IsEnabled = false;
                if (outlets.Count <= 1) { ScopeOutletRadio.IsEnabled = false; OutletBox.IsEnabled = false; }
            }

            // ----- Warehouses list (admins only; hide for others) -----
            if (isAdmin)
            {
                var warehouses = await db.Warehouses.AsNoTracking()
                                     .OrderBy(w => w.Name)
                                     .ToListAsync();

                WarehouseBox.ItemsSource = new[] { new Warehouse { Id = AllId, Name = "All Warehouses" } }
                                           .Concat(warehouses)
                                           .ToList();
                WarehouseBox.SelectedIndex = 0; // default All
                ScopeWarehouseRadio.IsEnabled = true;
                WarehouseBox.IsEnabled = true;
            }
            else
            {
                // For non-admins, force Outlet scope
                ScopeOutletRadio.IsChecked = true;
            }

            ApplyScopeEnablement();
        }

        private void ApplyScopeEnablement()
        {
            bool isOutlet = _scope == LocationScope.Outlet;

            if (OutletBox != null)
                OutletBox.IsEnabled = isOutlet && OutletBox.Items.Count > 0;

            if (WarehouseBox != null)
                WarehouseBox.IsEnabled = !isOutlet && WarehouseBox.Items.Count > 0;
        }


        private void ScopeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressScopeEvents) return;
            if (sender is not RadioButton rb) return;

            // Decide by which radio raised the event
            _scope = (rb == ScopeWarehouseRadio) ? LocationScope.Warehouse : LocationScope.Outlet;

            ApplyScopeEnablement();

            if (_scopeUiReady)
                ReloadCurrentView();   // avoid calling before combos are populated
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
            Grid.Columns.Add(new DataGridTextColumn { Header = "Variant", Binding = new Binding("Variant"), Width = 220 });
            Grid.Columns.Add(new DataGridTextColumn { Header = "On Hand", Binding = new Binding("OnHand"), Width = 90 });
        }

        private void ConfigureColumnsForProduct()
        {
            Grid.Columns.Clear();
            Grid.Columns.Add(new DataGridTextColumn { Header = "Product", Binding = new Binding("Product"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.Columns.Add(new DataGridTextColumn { Header = "On Hand", Binding = new Binding("OnHand"), Width = 120 });
        }

     

        private IQueryable<StockEntry> BuildScopedLedger(PosClientDbContext db)
            {
                var q = db.StockEntries.AsNoTracking();

                if (_scope == LocationScope.Outlet)
                {
                    var selOutlet = OutletBox.SelectedItem as Outlet;
                    if (selOutlet == null) return q.Where(s => false); // nothing selected yet

                    if (selOutlet.Id == AllId)
                    {
                        // All outlets = any row posted to Outlet locations
                        return q.Where(s => s.LocationType == InventoryLocationType.Outlet);
                    }
                    else
                    {
                        var outletId = selOutlet.Id;
                        return q.Where(s => s.LocationType == InventoryLocationType.Outlet
                                         && s.LocationId == outletId);
                    }
                }
                else // Warehouse scope
                {
                    var selWh = WarehouseBox.SelectedItem as Warehouse;
                    if (selWh == null) return q.Where(s => false);

                    if (selWh.Id == AllId)
                    {
                        // All warehouses
                        return q.Where(s => s.LocationType == InventoryLocationType.Warehouse);
                    }
                    else
                    {
                        var whId = selWh.Id;
                        return q.Where(s => s.LocationType == InventoryLocationType.Warehouse
                                         && s.LocationId == whId);
                }
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
                       join se in ledger on i.Id equals se.ItemId into gse
                       let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m
                       orderby (p != null ? p.Name : i.Name), i.Variant1Value, i.Variant2Value, i.Sku
                       select new
                       {
                           i.Sku,
                           ItemName = i.Name,
                           ProductName = p != null ? p.Name : null,
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
                          DisplayName = BuildDisplayName(x.ProductName, x.ItemName,
                                                         x.Variant1Name, x.Variant1Value,
                                                         x.Variant2Name, x.Variant2Value),
                          Variant = BuildVariant(x.Variant1Name, x.Variant1Value,
                                                 x.Variant2Name, x.Variant2Value),
                          OnHand = (int)Math.Round(x.OnHand)   // keep your grid int
                      })
                      .ToList();


            _itemRows = raw;

            ConfigureColumnsForItem();
            ApplySearchFilter();      // uses current SearchBox.Text
        }

        private void LoadDataByProduct()
        {
            using var db = new PosClientDbContext(_opts);

            var ledger = BuildScopedLedger(db);

            var rows =
                (from i in db.Items.AsNoTracking()
                 join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                 from p in gp.DefaultIfEmpty()
                 join se in ledger on i.Id equals se.ItemId into gse
                 let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m
                 group onHand by new { Prod = p != null ? p.Name : i.Name } into g
                 orderby g.Key.Prod
                 select new ProductRow
                 {
                     Product = g.Key.Prod,
                     OnHand = (int)Math.Round(g.Sum())   // sum is decimal, cast to int for your row
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
                    rows = rows.Where(r => ContainsIC(r.Product, term));

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


    }
}
