//Pos.Client.Wpf/StockReportWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class StockReportWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private const int OutletId = 1;

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

        public StockReportWindow()
        {
            InitializeComponent();

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

            // default view
            LoadDataByItemWithVariants();
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

        // ===== Data loads (full lists into cache) =====
        private void LoadDataByItemWithVariants()
        {
            using var db = new PosClientDbContext(_opts);

            var raw = (from i in db.Items.AsNoTracking()
                       join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                       from p in gp.DefaultIfEmpty()
                       join se in db.StockEntries.AsNoTracking().Where(s => s.OutletId == OutletId)
                            on i.Id equals se.ItemId into gse
                       let onHand = gse.Sum(x => (int?)x.QtyChange) ?? 0
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
                      .AsEnumerable() // compose display names client-side
                      .Select(x => new ItemRow
                      {
                          Sku = x.Sku,
                          DisplayName = BuildDisplayName(x.ProductName, x.ItemName,
                                                         x.Variant1Name, x.Variant1Value,
                                                         x.Variant2Name, x.Variant2Value),
                          Variant = BuildVariant(x.Variant1Name, x.Variant1Value,
                                                 x.Variant2Name, x.Variant2Value),
                          OnHand = x.OnHand
                      })
                      .ToList();

            _itemRows = raw;

            ConfigureColumnsForItem();
            ApplySearchFilter();      // uses current SearchBox.Text
        }

        private void LoadDataByProduct()
        {
            using var db = new PosClientDbContext(_opts);

            var rows =
                (from i in db.Items.AsNoTracking()
                 join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                 from p in gp.DefaultIfEmpty()
                 join se in db.StockEntries.AsNoTracking().Where(s => s.OutletId == OutletId)
                      on i.Id equals se.ItemId into gse
                 let onHand = gse.Sum(x => (int?)x.QtyChange) ?? 0
                 group onHand by new { Prod = p != null ? p.Name : i.Name } into g
                 orderby g.Key.Prod
                 select new ProductRow
                 {
                     Product = g.Key.Prod,
                     OnHand = g.Sum()
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
            if (e.Key == Key.Escape)
            {
                var now = DateTime.UtcNow;
                if (_lastEscDown.HasValue && (now - _lastEscDown.Value).TotalMilliseconds <= 600)
                {
                    Close();
                    return;
                }
                _lastEscDown = now;
                e.Handled = true; // swallow single Esc
                return;
            }

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
