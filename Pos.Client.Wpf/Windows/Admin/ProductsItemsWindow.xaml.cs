//Pos.Client.Wpf/Windows/Admin/ProductsItemsWindow.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Windows.Purchases; // for ItemQuickDialog
using Pos.Domain.Models;


namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ProductsItemsWindow : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private PosClientDbContext _db = null!;
        private CatalogService _svc = null!;

        private readonly ObservableCollection<Product> _products = new();
        private readonly ObservableCollection<ItemVariantRow> _standalone = new();
        private readonly ObservableCollection<ItemVariantRow> _gridItems = new();

        private Product? _selectedProduct;
        // at the top of ProductsItemsWindow class (fields)
        private bool _loadingProducts;    // protects loading the LEFT product list
        private bool _loadingVariants;    // protects loading the RIGHT grid for a selected product
        private bool _loadingStandalone;  // protects loading the standalone items tab

        // optional (nice to have)
        private bool _saving;             // protects Save Changes
        private bool _addingVariants;     // protects Add Variants
        private bool _userClickingProducts;
        private enum RightMode { Variants, Standalone }
        private RightMode _mode;

        private readonly ObservableCollection<Brand> _brands = new();
        private readonly ObservableCollection<Category> _categories = new();
        private ItemVariantRow? SelectedRow => VariantsGrid.SelectedItem as ItemVariantRow;
        public ProductsItemsWindow()
        {
            InitializeComponent();

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();

            ProductsList.ItemsSource = _products;
            StandaloneList.ItemsSource = _standalone;
            VariantsGrid.ItemsSource = _gridItems;
         
            Loaded += async (_, __) =>
            {
                await using var db = await _dbf.CreateDbContextAsync(); // ensure DB is ready
                _brands.Clear();
                foreach (var b in await db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync())
                    _brands.Add(b);

                _categories.Clear();
                foreach (var c in await db.Categories.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync())
                    _categories.Add(c);

                LeftTabs.SelectedIndex = 0; // default: Products
                await RefreshAsync();
            };
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null && current is not T)
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            return current as T;
        }

        private void SetRightGridMode(RightMode mode)
        {
            _mode = mode;

            // Variants: hide Name (same as product), show variant axes
            ColName.Visibility = (mode == RightMode.Variants) ? Visibility.Collapsed : Visibility.Visible;

            ColVar1Name.Visibility = (mode == RightMode.Variants) ? Visibility.Visible : Visibility.Collapsed;
            ColVar1Value.Visibility = (mode == RightMode.Variants) ? Visibility.Visible : Visibility.Collapsed;
            ColVar2Name.Visibility = (mode == RightMode.Variants) ? Visibility.Visible : Visibility.Collapsed;
            ColVar2Value.Visibility = (mode == RightMode.Variants) ? Visibility.Visible : Visibility.Collapsed;

            // Brand/Category/Tax/Discount: useful in both modes, keep visible
            ColBrand.Visibility = Visibility.Visible;
            ColCategory.Visibility = Visibility.Visible;
            ColTaxCode.Visibility = Visibility.Visible;
            ColTaxPct.Visibility = Visibility.Visible;
            ColTaxIncl.Visibility = Visibility.Visible;
            ColDiscPct.Visibility = Visibility.Visible;
            ColDiscAmt.Visibility = Visibility.Visible;
        }

        private async Task OpenDbAsync()
        {
            _db?.Dispose();
            _db = await _dbf.CreateDbContextAsync();
            _svc = new CatalogService(_db);
        }

        private async Task RefreshAsync()
        {
            if (LeftTabs.SelectedIndex == 0)
                await RefreshProductsAsync();
            else
                await RefreshStandaloneAsync();
        }

        private async Task RefreshProductsAsync()
        {
            if (_loadingProducts) return;
            _loadingProducts = true;
            try
            {
                await OpenDbAsync();

                var term = SearchBox.Text?.Trim() ?? "";
                var list = await _svc.SearchProductsAsync(term);

                // remember selected Id (if any)
                int? selectedId = (_selectedProduct ?? ProductsList.SelectedItem as Product)?.Id;

                _products.Clear();
                foreach (var p in list) _products.Add(p);

                // restore previous selection only if user isn't clicking right now
                if (!_userClickingProducts && selectedId != null)
                {
                    var again = _products.FirstOrDefault(x => x.Id == selectedId);
                    if (again != null) ProductsList.SelectedItem = again;
                }
                // IMPORTANT: do NOT force-select index 0 here; let the user click.
            }
            finally
            {
                _loadingProducts = false;
            }

            // Load variants once the list has settled and user isn't mid-click
            if (!_userClickingProducts)
                await LoadSelectedProductItemsAsync();
        }


        private void ProductsList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _userClickingProducts = true;
        }

        private async void ProductsList_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _userClickingProducts = false;

            // Find the ListBoxItem that was clicked
            var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var product = container?.DataContext as Product;

            if (container != null && !container.IsSelected)
                container.IsSelected = true;   // keep UI in sync

            // Load variants for the product that was actually clicked (even if SelectedItem lags)
            await LoadVariantsForProduct(product ?? (ProductsList.SelectedItem as Product));
        }




        

        // wrapper that uses the current SelectedItem
        private Task LoadSelectedProductItemsAsync()
            => LoadVariantsForProduct(ProductsList.SelectedItem as Product);


        private async Task LoadVariantsForProduct(Product? product)
        {
            if (_loadingVariants) return;
            _loadingVariants = true;
            try
            {
                _selectedProduct = product;
                _gridItems.Clear();

                if (product == null)
                {
                    GridTitle.Text = "Variants";
                    SetRightGridMode(RightMode.Variants);
                    return;
                }

                var rows = await _svc.GetItemsForProductAsync(product.Id);
                foreach (var r in rows) _gridItems.Add(r);

                GridTitle.Text = $"Variants — {product.Name}";
                SetRightGridMode(RightMode.Variants);
            }
            finally { _loadingVariants = false; }
        }

        private async Task RefreshStandaloneAsync()
        {
            if (_loadingStandalone) return;
            _loadingStandalone = true;
            try
            {
                await OpenDbAsync();
                var term = SearchBox.Text?.Trim() ?? "";
                var rows = await _svc.SearchStandaloneItemRowsAsync(term);

                _standalone.Clear();
                foreach (var r in rows) _standalone.Add(r);

                _gridItems.Clear();
                foreach (var r in rows) _gridItems.Add(r);

                GridTitle.Text = "Standalone Items";
                SetRightGridMode(RightMode.Standalone);
            }
            finally { _loadingStandalone = false; }
        }





        // No-op: the right grid already shows all standalone matches
        private void StandaloneList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // intentionally empty
        }



        // --- UI events ---
        private async void Find_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

      

        private async void ProductsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await LoadSelectedProductItemsAsync();
        }


        private async void LeftTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await RefreshAsync(); // RefreshAsync decides Products vs Standalone and those methods are guarded
        }

        // --- Commands ---
        private async void NewProduct_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProductNameDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            await OpenDbAsync();
            // AFTER
            var p = await _svc.CreateProductAsync(dlg.ProductName, dlg.BrandId, dlg.CategoryId);
            _products.Insert(0, p);
            ProductsList.SelectedItem = p;
        }

        private async void AddVariants_Click(object sender, RoutedEventArgs e)
        {
            if (LeftTabs.SelectedIndex != 0)
            {
                MessageBox.Show("Switch to the Products tab and select a product first.");
                return;
            }

            var product = ProductsList.SelectedItem as Product;
            if (product == null)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            var dlg = new VariantBatchDialog { Owner = this };
            dlg.PrefillProduct(product);
            if (dlg.ShowDialog() != true) return;

            await OpenDbAsync();
            var created = await _svc.BulkCreateVariantsAsync(
                product.Id, product.Name,
                dlg.Axis1Name, dlg.Axis1Values,
                dlg.Axis2Name, dlg.Axis2Values,
                dlg.Price, dlg.TaxCode, dlg.TaxPct, dlg.TaxInclusive,
                dlg.DefaultDiscPct, dlg.DefaultDiscAmt
            );

            foreach (var it in created)
            {
                _gridItems.Add(new Pos.Domain.Models.ItemVariantRow
                {
                    Id = it.Id,
                    Sku = it.Sku,
                    Name = it.Name,
                    ProductName = product.Name,
                    Barcode = it.Barcode,
                    Price = it.Price,
                    Variant1Name = it.Variant1Name,
                    Variant1Value = it.Variant1Value,
                    Variant2Name = it.Variant2Name,
                    Variant2Value = it.Variant2Value,
                    UpdatedAt = it.UpdatedAt
                });
            }
        }




        private async void NewStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ItemQuickDialog { Owner = this, Title = "New Standalone Item" };
            if (dlg.ShowDialog() != true) return;

            await OpenDbAsync();
            var now = DateTime.UtcNow;

            // create the entity
            var entity = new Item
            {
                ProductId = null,
                Sku = dlg.Sku,
                Name = dlg.NameVal,
                Barcode = dlg.BarcodeVal,
                Price = dlg.PriceVal,
                UpdatedAt = now,
                TaxCode = dlg.TaxCodeVal,
                DefaultTaxRatePct = dlg.TaxPctVal,
                TaxInclusive = dlg.TaxInclusiveVal,
                DefaultDiscountPct = dlg.DiscountPctVal,
                DefaultDiscountAmt = dlg.DiscountAmtVal,
                Variant1Name = dlg.Variant1NameVal,
                Variant1Value = dlg.Variant1ValueVal,
                Variant2Name = dlg.Variant2NameVal,
                Variant2Value = dlg.Variant2ValueVal
            };

            entity = await _svc.CreateItemAsync(entity);

            // map to DTO row for UI lists
            var row = new ItemVariantRow
            {
                Id = entity.Id,
                Sku = entity.Sku,
                Name = entity.Name,
                ProductName = null,
                Barcode = entity.Barcode,
                Price = entity.Price,
                Variant1Name = entity.Variant1Name,
                Variant1Value = entity.Variant1Value,
                Variant2Name = entity.Variant2Name,
                Variant2Value = entity.Variant2Value,
                UpdatedAt = entity.UpdatedAt
            };

            if (LeftTabs.SelectedIndex == 1)
            {
                _standalone.Insert(0, row);
                _gridItems.Insert(0, row);
            }

            MessageBox.Show("Item created.");
        }


        private async void SaveVariants_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;
            _saving = true;
            try
            {
                await OpenDbAsync();

                foreach (var r in _gridItems)
                {
                    var entity = await _db.Items.FirstAsync(i => i.Id == r.Id);

                    entity.Sku = r.Sku;
                    entity.Barcode = r.Barcode ?? "";
                    entity.Name = r.Name;
                    entity.Price = r.Price;

                    entity.Variant1Name = r.Variant1Name;
                    entity.Variant1Value = r.Variant1Value;
                    entity.Variant2Name = r.Variant2Name;
                    entity.Variant2Value = r.Variant2Value;

                    // NEW: Brand/Category
                    entity.BrandId = r.BrandId;
                    entity.CategoryId = r.CategoryId;

                    // NEW: Tax / Discounts
                    entity.TaxCode = r.TaxCode;
                    entity.DefaultTaxRatePct = r.DefaultTaxRatePct;
                    entity.TaxInclusive = r.TaxInclusive;
                    entity.DefaultDiscountPct = r.DefaultDiscountPct;
                    entity.DefaultDiscountAmt = r.DefaultDiscountAmt;

                    entity.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                MessageBox.Show("Changes saved.");
            }
            finally { _saving = false; }
        }

        private async void DeleteProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not Product p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            await OpenDbAsync();
            var (can, reason) = await _svc.CanHardDeleteProductAsync(p.Id);
            if (!can)
            {
                MessageBox.Show($"Cannot delete product.\nReason: {reason}\n\nYou can Void it instead.");
                return;
            }

            if (MessageBox.Show($"Delete '{p.Name}' permanently? This cannot be undone.",
                                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            await _svc.DeleteProductAsync(p.Id);
            await RefreshAsync();
            MessageBox.Show("Product deleted.");
        }

        private async void VoidProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not Product p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            if (MessageBox.Show($"Void '{p.Name}'? It will be disabled everywhere but remain in history.",
                                "Confirm Void", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await OpenDbAsync();
            await _svc.VoidProductAsync(p.Id, user: "admin"); // TODO: current user
            await RefreshAsync();
            MessageBox.Show("Product voided.");
        }

        

        private async void DeleteStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null || row.Id == 0 || row.ProductName != null)
            {
                MessageBox.Show("Pick a standalone item row to delete.");
                return;
            }

            await OpenDbAsync();
            var (can, reason) = await _svc.CanHardDeleteItemAsync(row.Id);
            if (!can)
            {
                MessageBox.Show($"Cannot delete item.\nReason: {reason}\n\nYou can Void it instead.");
                return;
            }

            if (MessageBox.Show($"Delete item '{row.Name}' permanently?",
                                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            await _svc.DeleteItemAsync(row.Id);
            await RefreshAsync();
            MessageBox.Show("Item deleted.");
        }

        private async void VoidStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null || row.Id == 0)
            {
                MessageBox.Show("Pick an item row to void.");
                return;
            }

            await OpenDbAsync();
            await _svc.VoidItemAsync(row.Id, user: "admin");
            await RefreshAsync();
            MessageBox.Show("Item voided.");
        }



    }
}
