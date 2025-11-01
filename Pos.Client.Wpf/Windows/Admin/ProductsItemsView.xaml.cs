// Pos.Client.Wpf/Windows/Admin/ProductsItemsWindow.xaml.cs
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
using Pos.Client.Wpf.Windows.Purchases; // ItemQuickDialog
using Pos.Domain.Models;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ProductsItemsView : UserControl
    {
        // ----- services / db -----
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private PosClientDbContext _db = null!;
        private CatalogService _svc = null!;
        private bool _squelchGridEdits;


        // ----- tracking originals & dirties -----
        private readonly Dictionary<int, ItemVariantRow> _originals = new(); // for variants list
        private readonly HashSet<int> _dirtyIds = new();                      // changed variant rows
        private ItemVariantRow? _standaloneOriginal;                          // snapshot of selected standalone
        private bool _standaloneDirty;                                        // flag for standalone


        // ----- left lists -----
        private readonly ObservableCollection<Product> _products = new();
        private readonly ObservableCollection<ItemVariantRow> _standalone = new();
        // ----- right grid (backing source) -----
        private readonly ObservableCollection<ItemVariantRow> _gridItems = new();
        private Product? _selectedProduct;
        // flow flags
        private bool _loadingProducts;
        private bool _loadingVariants;
        private bool _loadingStandalone;
        private bool _saving;
        private enum RightMode { Variants, Standalone }
        private RightMode _mode;
        // lookup sources (PUBLIC for binding)
        private readonly ObservableCollection<Brand> _brands = new();
        private readonly ObservableCollection<Category> _categories = new();
        public ObservableCollection<Brand> Brands => _brands;
        public ObservableCollection<Category> Categories => _categories;
        private ItemVariantRow? SelectedRow => VariantsGrid.SelectedItem as ItemVariantRow;

        public ProductsItemsView()
        {
            InitializeComponent();
            // IMPORTANT: let XAML bindings see Brands/Categories
            DataContext = this;
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            ProductsList.ItemsSource = _products;
            StandaloneList.ItemsSource = _standalone;
            VariantsGrid.ItemsSource = _gridItems;
            Loaded += async (_, __) =>
            {
                await using var db = await _dbf.CreateDbContextAsync();
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

        private async Task OpenDbAsync()
        {
            _db?.Dispose();
            _db = await _dbf.CreateDbContextAsync();
            _svc = new CatalogService(_db);
        }

        private void SetRightGridMode(RightMode mode)
        {
            _mode = mode;
            var isVariants = mode == RightMode.Variants;

            ColName.Visibility = isVariants ? Visibility.Collapsed : Visibility.Visible;
            ColVar1Name.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            ColVar1Value.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            ColVar2Name.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            ColVar2Value.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            ColBrand.Visibility = isVariants ? Visibility.Collapsed : Visibility.Visible;
            ColCategory.Visibility = isVariants ? Visibility.Collapsed : Visibility.Visible;

            if (BtnAddVariant != null) BtnAddVariant.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            if (BtnDeleteVoidItem != null) BtnDeleteVoidItem.Tag = isVariants;
            if (BtnEditVariant != null) BtnEditVariant.Tag = isVariants;
            if (BtnDeleteVoidVariant != null) BtnDeleteVoidVariant.Tag = isVariants;
            if (ColBarcode != null) ColBarcode.IsReadOnly = true;

            // Always useful on both modes
            ColTaxCode.Visibility = Visibility.Visible;
            ColTaxPct.Visibility = Visibility.Visible;
            ColTaxIncl.Visibility = Visibility.Visible;
            ColDiscPct.Visibility = Visibility.Visible;
            ColDiscAmt.Visibility = Visibility.Visible;
            UpdateSaveButtonVisibility();

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
                // remember selected id
                int? selectedId = (ProductsList.SelectedItem as Product)?.Id;
                _products.Clear();
                foreach (var p in list) _products.Add(p);
                // restore selection
                if (selectedId is int id)
                {
                    var again = _products.FirstOrDefault(x => x.Id == id);
                    if (again != null) ProductsList.SelectedItem = again;
                }
            }
            finally { _loadingProducts = false; }
            await LoadSelectedProductItemsAsync();
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

                // remember current selection
                int? selectedId = (StandaloneList.SelectedItem as ItemVariantRow)?.Id;

                // repopulate source
                _standalone.Clear();
                foreach (var r in rows) _standalone.Add(r);

                // try to restore the selected row by ID
                ItemVariantRow? restored = null;
                if (selectedId is int id)
                    restored = _standalone.FirstOrDefault(x => x.Id == id);

                // IMPORTANT: set the ListBox selection so the highlight shows
                if (restored != null)
                {
                    StandaloneList.SelectedItem = restored;
                    StandaloneList.UpdateLayout();
                    StandaloneList.ScrollIntoView(restored);

                    // Since SelectionChanged is suppressed during loading, update the right grid manually
                    _gridItems.Clear();
                    _gridItems.Add(restored);
                    GridTitle.Text = $"Standalone Item — {restored.Name}";

                    _standaloneOriginal = CloneRow(restored);
                    _standaloneDirty = false;
                }
                else
                {
                    // no selection; keep right grid empty
                    StandaloneList.SelectedItem = null;
                    _gridItems.Clear();
                    GridTitle.Text = "Standalone Items — (no selection)";
                    _standaloneOriginal = null;
                    _standaloneDirty = false;
                }

                // mode / buttons / save visibility
                SetRightGridMode(RightMode.Standalone);
                _originals.Clear();
                _dirtyIds.Clear();
                UpdateSaveButtonVisibility();
            }
            finally { _loadingStandalone = false; }
        }



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
                _originals.Clear();
                _dirtyIds.Clear();
                _standaloneOriginal = null;
                _standaloneDirty = false;

                if (product == null)
                {
                    GridTitle.Text = "Variants — (no product selected)";
                    SetRightGridMode(RightMode.Variants);
                    UpdateSaveButtonVisibility();
                    return;
                }

                var rows = await _svc.GetItemsForProductAsync(product.Id);
                foreach (var r in rows)
                {
                    _gridItems.Add(r);
                    if (r.Id > 0)
                        _originals[r.Id] = CloneRow(r);
                }
                GridTitle.Text = $"Variants — {product.Name}";
                SetRightGridMode(RightMode.Variants);
                UpdateSaveButtonVisibility();
            }
            finally { _loadingVariants = false; }
        }


        private void StandaloneItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem lbi)
            {
                if (!lbi.IsSelected)
                    lbi.IsSelected = true;
                e.Handled = false;
            }
        }

        // ---------------- UI events ----------------
        private async void Find_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async void LeftTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await RefreshAsync();
        }

        private async void ProductsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await LoadSelectedProductItemsAsync();
        }

        private async void NewProduct_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProductNameDialog { };
            if (dlg.ShowDialog() != true) return;

            await OpenDbAsync();
            var p = await _svc.CreateProductAsync(dlg.ProductName, dlg.BrandId, dlg.CategoryId);
            _products.Insert(0, p);
            ProductsList.SelectedItem = p;
        }

        // EDIT product (same dialog you use to create, now prefilled)
        private async void EditProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not Product p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }
            var dlg = new ProductNameDialog { };
            dlg.Prefill(p.Name, p.BrandId, p.CategoryId); // ensure your dialog exposes a Prefill method
            if (dlg.ShowDialog() != true) return;
            await OpenDbAsync();
            // update via service (you likely have this; otherwise do direct EF)
            var updated = await _svc.UpdateProductAsync(p.Id, dlg.ProductName, dlg.BrandId, dlg.CategoryId);
            // reflect in UI list
            var inList = _products.FirstOrDefault(x => x.Id == p.Id);
            if (inList != null)
            {
                inList.Name = updated.Name;
                inList.BrandId = updated.BrandId;
                inList.CategoryId = updated.CategoryId;
                // if Product has Brand/Category navigation loaded, you might also refresh them:
                inList.Brand = updated.Brand;
                inList.Category = updated.Category;
            }

            // refresh right grid title
            if (_selectedProduct?.Id == p.Id)
                GridTitle.Text = $"Variants — {updated.Name}";
        }

        private async void AddVariants_Click(object sender, RoutedEventArgs e)
        {
            var product = _selectedProduct ?? ProductsList.SelectedItem as Product;
            if (product == null) { MessageBox.Show("Select a product first."); return; }
            // Make sure the right grid is in Variants mode before we start adding
            GridTitle.Text = $"Variants — {product.Name}";
            SetRightGridMode(RightMode.Variants);
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.Sequential)
            {
                SaveImmediately = true, // <-- this makes "Save & Add another" call the saver below
                SaveOneAsync = async (item) =>
                {
                    try
                    {
                        var saved = await _svc.CreateItemAsync(item);
                        var primary = saved.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                            ?? saved.Barcodes?.FirstOrDefault()?.Code
                            ?? "";
                        Dispatcher.Invoke(() =>
                        {
                            _gridItems.Add(new ItemVariantRow
                            {
                                Id = saved.Id,
                                Sku = saved.Sku,
                                Name = saved.Name,
                                ProductName = product.Name,
                                Barcode = primary,
                                Price = saved.Price,
                                Variant1Name = saved.Variant1Name,
                                Variant1Value = saved.Variant1Value,
                                Variant2Name = saved.Variant2Name,
                                Variant2Value = saved.Variant2Value,
                                TaxCode = saved.TaxCode,
                                DefaultTaxRatePct = saved.DefaultTaxRatePct,
                                TaxInclusive = saved.TaxInclusive,
                                DefaultDiscountPct = saved.DefaultDiscountPct,
                                DefaultDiscountAmt = saved.DefaultDiscountAmt,
                                UpdatedAt = saved.UpdatedAt,
                                IsActive = saved.IsActive
                            });
                        });

                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Save failed: " + ex.Message);
                        return false;
                    }
                }
            };

            dlg.PrefillProduct(product);
            if (dlg.ShowDialog() != true) return;
            if (dlg.CreatedItems is { Count: > 0 })
            {
                await OpenDbAsync();

                foreach (var v in dlg.CreatedItems)
                {
                    var saved = await _svc.CreateItemAsync(v);
                    var primary = saved.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                      ?? saved.Barcodes?.FirstOrDefault()?.Code
                      ?? "";

                    _gridItems.Add(new ItemVariantRow
                    {
                        Id = saved.Id,
                        Sku = saved.Sku,
                        Name = saved.Name,
                        ProductName = product.Name,
                        Barcode = primary,
                        Price = saved.Price,
                        Variant1Name = saved.Variant1Name,
                        Variant1Value = saved.Variant1Value,
                        Variant2Name = saved.Variant2Name,
                        Variant2Value = saved.Variant2Value,
                        TaxCode = saved.TaxCode,
                        DefaultTaxRatePct = saved.DefaultTaxRatePct,
                        TaxInclusive = saved.TaxInclusive,
                        DefaultDiscountPct = saved.DefaultDiscountPct,
                        DefaultDiscountAmt = saved.DefaultDiscountAmt,
                        UpdatedAt = saved.UpdatedAt,
                        IsActive = saved.IsActive
                    });
                }

                MessageBox.Show("Variants added.");
            }
        }

        private async void NewStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            await OpenDbAsync();
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.Sequential)
            {
                //Owner = this,
                SaveImmediately = true,
                SaveOneAsync = async (item) =>
                {
                    try
                    {
                        var saved = await _svc.CreateItemAsync(item);
                        var primary = saved.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                                      ?? saved.Barcodes?.FirstOrDefault()?.Code
                                      ?? "";
                        // add into left + right panes just like you already do
                        var row = new ItemVariantRow
                        {
                            Id = saved.Id,
                            Sku = saved.Sku,
                            Name = saved.Name,
                            ProductName = null,       // standalone
                            Barcode = primary,
                            Price = saved.Price,
                            Variant1Name = saved.Variant1Name,
                            Variant1Value = saved.Variant1Value,
                            Variant2Name = saved.Variant2Name,
                            Variant2Value = saved.Variant2Value,
                            TaxCode = saved.TaxCode,
                            DefaultTaxRatePct = saved.DefaultTaxRatePct,
                            TaxInclusive = saved.TaxInclusive,
                            DefaultDiscountPct = saved.DefaultDiscountPct,
                            DefaultDiscountAmt = saved.DefaultDiscountAmt,
                            UpdatedAt = saved.UpdatedAt,
                            IsActive = saved.IsActive
                        };

                        _standalone.Insert(0, row);
                        _gridItems.Clear();
                        _gridItems.Add(row);
                        GridTitle.Text = $"Standalone Item — {row.Name}";
                        SetRightGridMode(RightMode.Standalone);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Save failed: " + ex.Message);
                        return false;
                    }
                }
            };
            dlg.PrefillStandalone();
            if (dlg.ShowDialog() == true && dlg.CreatedItems?.Count > 0)
            {
                foreach (var v in dlg.CreatedItems)
                {
                    var saved = await _svc.CreateItemAsync(v);
                    var primary = saved.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                                  ?? saved.Barcodes?.FirstOrDefault()?.Code
                                  ?? "";
                    var row = new ItemVariantRow
                    {
                        Id = saved.Id,
                        Sku = saved.Sku,
                        Name = saved.Name,
                        ProductName = null,
                        Barcode = primary,
                        Price = saved.Price,
                        Variant1Name = saved.Variant1Name,
                        Variant1Value = saved.Variant1Value,
                        Variant2Name = saved.Variant2Name,
                        Variant2Value = saved.Variant2Value,
                        TaxCode = saved.TaxCode,
                        DefaultTaxRatePct = saved.DefaultTaxRatePct,
                        TaxInclusive = saved.TaxInclusive,
                        DefaultDiscountPct = saved.DefaultDiscountPct,
                        DefaultDiscountAmt = saved.DefaultDiscountAmt,
                        UpdatedAt = saved.UpdatedAt,
                        IsActive = saved.IsActive
                    };
                    _standalone.Insert(0, row);
                    _gridItems.Clear();
                    _gridItems.Add(row);
                    GridTitle.Text = $"Standalone Item — {row.Name}";
                    SetRightGridMode(RightMode.Standalone);
                }
                MessageBox.Show("Item(s) added.");
            }
        }

        private async void SaveVariants_Click(object sender, RoutedEventArgs e)
        {
            if (_saving) return;
            _saving = true;
            try
            {
                await OpenDbAsync();

                // Decide which rows to persist
                IEnumerable<ItemVariantRow> toSave;
                if (_mode == RightMode.Standalone)
                {
                    if (!_standaloneDirty)
                    {
                        MessageBox.Show("No changes to save.");
                        return;
                    }
                    toSave = _gridItems.ToList(); // only one row is present in Standalone mode
                }
                else
                {
                    if (_dirtyIds.Count == 0)
                    {
                        MessageBox.Show("No changes to save.");
                        return;
                    }
                    var set = _dirtyIds.ToHashSet();
                    toSave = _gridItems.Where(r => set.Contains(r.Id)).ToList();
                }

                foreach (var r in toSave)
                {
                    var entity = await _db.Items.FirstAsync(i => i.Id == r.Id);

                    // IMPORTANT: do *not* modify barcodes here (locked in grid).
                    // They are edited in the Edit dialog you already wired.

                    entity.Sku = r.Sku;
                    entity.Name = r.Name; // shown for standalone; hidden in variants grid but still persisted if changed via dialog
                    entity.Price = r.Price;
                    entity.Variant1Name = r.Variant1Name;
                    entity.Variant1Value = r.Variant1Value;
                    entity.Variant2Name = r.Variant2Name;
                    entity.Variant2Value = r.Variant2Value;
                    entity.BrandId = r.BrandId;
                    entity.CategoryId = r.CategoryId;
                    entity.TaxCode = r.TaxCode;
                    entity.DefaultTaxRatePct = r.DefaultTaxRatePct;
                    entity.TaxInclusive = r.TaxInclusive;
                    entity.DefaultDiscountPct = r.DefaultDiscountPct;
                    entity.DefaultDiscountAmt = r.DefaultDiscountAmt;
                    entity.IsActive = r.IsActive;
                    entity.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();

                // Refresh snapshots for rows we saved
                if (_mode == RightMode.Standalone)
                {
                    if (_gridItems.FirstOrDefault() is ItemVariantRow cur)
                    {
                        _standaloneOriginal = CloneRow(cur);
                        _standaloneDirty = false;
                    }
                }
                else
                {
                    foreach (var r in toSave)
                        _originals[r.Id] = CloneRow(r);
                    _dirtyIds.Clear();
                }

                UpdateSaveButtonVisibility();
                MessageBox.Show("Changes saved.");
            }
            finally { _saving = false; }
        }


        private async void EditStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            var row = StandaloneList.SelectedItem as ItemVariantRow;
            if (row == null || row.Id <= 0 || row.ProductName != null)
            {
                MessageBox.Show("Select a standalone item first.");
                return;
            }
            await OpenDbAsync();
            var entity = await _db.Items
                .Include(i => i.Barcodes)
                .FirstOrDefaultAsync(i => i.Id == row.Id);
            if (entity == null)
            {
                MessageBox.Show("Could not load the selected item.");
                return;
            }
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.EditSingle) { };
            await dlg.PrefillStandaloneForEditAsync(entity);   // 👈 single, consistent entry-point

            if (dlg.ShowDialog() != true) return;
            var edited = dlg.CreatedItems?.FirstOrDefault();
            if (edited == null)
            {
                MessageBox.Show("No changes returned.");
                return;
            }

            try
            {
                await OpenDbAsync();
                var dbItem = await _db.Items
                    .Include(i => i.Barcodes)
                    .FirstAsync(i => i.Id == entity.Id);
                var newCodes = edited.Barcodes?
                    .Select(b => b.Code.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new();
                bool conflict = await _db.ItemBarcodes
                    .AnyAsync(b => newCodes.Contains(b.Code) && b.ItemId != dbItem.Id);
                if (conflict)
                    throw new InvalidOperationException("One or more barcodes are already used by another item.");
                dbItem.Sku = edited.Sku ?? "";
                dbItem.Name = string.IsNullOrWhiteSpace(edited.Name) ? dbItem.Name : edited.Name;
                dbItem.Price = edited.Price;
                dbItem.TaxCode = edited.TaxCode;
                dbItem.DefaultTaxRatePct = edited.DefaultTaxRatePct;
                dbItem.TaxInclusive = edited.TaxInclusive;
                dbItem.DefaultDiscountPct = edited.DefaultDiscountPct;
                dbItem.DefaultDiscountAmt = edited.DefaultDiscountAmt;
                dbItem.Variant1Name = edited.Variant1Name;
                dbItem.Variant1Value = edited.Variant1Value;
                dbItem.Variant2Name = edited.Variant2Name;
                dbItem.Variant2Value = edited.Variant2Value;
                dbItem.IsActive = edited.IsActive;
                dbItem.UpdatedAt = DateTime.UtcNow;
                _db.ItemBarcodes.RemoveRange(dbItem.Barcodes);
                dbItem.Barcodes.Clear();
                foreach (var b in edited.Barcodes ?? Enumerable.Empty<ItemBarcode>())
                {
                    dbItem.Barcodes.Add(new ItemBarcode
                    {
                        ItemId = dbItem.Id,
                        Code = b.Code.Trim(),
                        Symbology = b.Symbology,
                        QuantityPerScan = Math.Max(1, b.QuantityPerScan),
                        IsPrimary = b.IsPrimary,
                        Label = string.IsNullOrWhiteSpace(b.Label) ? null : b.Label,
                        CreatedAt = b.CreatedAt == default ? DateTime.UtcNow : b.CreatedAt,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                if (dbItem.Barcodes.Any() && !dbItem.Barcodes.Any(x => x.IsPrimary))
                    dbItem.Barcodes.First().IsPrimary = true;
                await _db.SaveChangesAsync();
                var primary = dbItem.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                              ?? dbItem.Barcodes?.FirstOrDefault()?.Code
                              ?? "";
                var idx = _gridItems.IndexOf(row);
                if (idx >= 0)
                {
                    _gridItems[idx] = new ItemVariantRow
                    {
                        Id = dbItem.Id,
                        Sku = dbItem.Sku,
                        Name = dbItem.Name,
                        ProductName = null, // standalone
                        Barcode = primary,
                        Price = dbItem.Price,
                        Variant1Name = dbItem.Variant1Name,
                        Variant1Value = dbItem.Variant1Value,
                        Variant2Name = dbItem.Variant2Name,
                        Variant2Value = dbItem.Variant2Value,
                        TaxCode = dbItem.TaxCode,
                        DefaultTaxRatePct = dbItem.DefaultTaxRatePct,
                        TaxInclusive = dbItem.TaxInclusive,
                        DefaultDiscountPct = dbItem.DefaultDiscountPct,
                        DefaultDiscountAmt = dbItem.DefaultDiscountAmt,
                        UpdatedAt = dbItem.UpdatedAt,
                        IsActive = dbItem.IsActive,
                        IsVoided = dbItem.IsVoided
                    };
                }
                MessageBox.Show("Item updated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Update failed: " + ex.Message);
            }
        }

        private async void DeleteOrVoidProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not Product p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }
            await OpenDbAsync();
            var (canDelete, reason) = await _svc.CanHardDeleteProductAsync(p.Id);
            var msg = canDelete
                ? $"What do you want to do with '{p.Name}'?\n\nYes = Delete permanently\nNo = Void (disable)\nCancel = Do nothing"
                : $"This product has history and cannot be deleted.\n\nReason: {reason}\n\nDo you want to Void it?\nYes = Void\nNo/Cancel = Do nothing";
            var result = MessageBox.Show(msg,
                                         canDelete ? "Delete or Void Product" : "Void Product",
                                         canDelete ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo,
                                         canDelete ? MessageBoxImage.Question : MessageBoxImage.Information);

            if (!canDelete)
            {
                if (result == MessageBoxResult.Yes)
                {
                    await _svc.VoidProductAsync(p.Id, user: "admin");
                    await RefreshAsync();
                    MessageBox.Show("Product voided.");
                }
                return;
            }
            if (result == MessageBoxResult.Yes)
            {
                if (MessageBox.Show($"Delete '{p.Name}' permanently? This cannot be undone.",
                                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                await _svc.DeleteProductAsync(p.Id);
                await RefreshAsync();
                MessageBox.Show("Product deleted.");
            }
            else if (result == MessageBoxResult.No)
            {
                await _svc.VoidProductAsync(p.Id, user: "admin");
                await RefreshAsync();
                MessageBox.Show("Product voided.");
            }
        }

        private async void DeleteOrVoidVariant_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null || row.Id == 0)
            {
                MessageBox.Show("Pick a row first.");
                return;
            }
            await OpenDbAsync();
            var (canDelete, reason) = await _svc.CanHardDeleteItemAsync(row.Id);
            var label = row.ProductName == null ? "item" : "variant";
            var title = $"Delete or Void {label}";
            var msg = canDelete
                ? $"What do you want to do with '{row.Name}'?\n\nYes = Delete permanently\nNo = Void (disable)\nCancel = Do nothing"
                : $"This {label} has history and cannot be deleted.\n\nReason: {reason}\n\nDo you want to Void it?\nYes = Void\nNo/Cancel = Do nothing";
            var result = MessageBox.Show(msg,
                                         title,
                                         canDelete ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo,
                                         MessageBoxImage.Question);
            if (!canDelete)
            {
                if (result == MessageBoxResult.Yes)
                {
                    await _svc.VoidItemAsync(row.Id, user: "admin");
                    await RefreshAsync();
                    MessageBox.Show($"{label.First().ToString().ToUpper() + label.Substring(1)} voided.");
                }
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                if (MessageBox.Show($"Delete '{row.Name}' permanently? This cannot be undone.",
                                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                await _svc.DeleteItemAsync(row.Id);
                await RefreshAsync();
                MessageBox.Show($"{label.First().ToString().ToUpper() + label.Substring(1)} deleted.");
            }
            else if (result == MessageBoxResult.No)
            {
                await _svc.VoidItemAsync(row.Id, user: "admin");
                await RefreshAsync();
                MessageBox.Show($"{label.First().ToString().ToUpper() + label.Substring(1)} voided.");
            }
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

        private void Brands_Click(object sender, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<BrandsWindow>();
            //w.Owner = this;
            w.ShowDialog();
        }

        private void Categories_Click(object sender, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<CategoriesWindow>();
            //w.Owner = this;
            w.ShowDialog();
        }
        private async void StandaloneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingStandalone) return;
            _gridItems.Clear();
            if (StandaloneList.SelectedItem is ItemVariantRow row)
            {
                _gridItems.Add(row);
                GridTitle.Text = $"Standalone Item — {row.Name}";
                _standaloneOriginal = CloneRow(row);
                _standaloneDirty = false;
            }
            else
            {
                GridTitle.Text = "Standalone Items — (no selection)";
                _standaloneOriginal = null;
                _standaloneDirty = false;
            }
            SetRightGridMode(RightMode.Standalone);
            _originals.Clear();
            _dirtyIds.Clear();
            UpdateSaveButtonVisibility();
            await Task.CompletedTask;
        }


        private void StandaloneList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ItemsControl ic) return;
            var item = ItemsControl.ContainerFromElement(ic, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null) return;
            if (!item.IsSelected)
                item.IsSelected = true;
            e.Handled = true;
        }

        private void VariantsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_squelchGridEdits) return;

            if (e.EditAction == DataGridEditAction.Commit)
            {
                // DO NOT call CommitEdit() here – it re-enters CellEditEnding.
                _squelchGridEdits = true;
                try
                {
                    // Defer until after the edit actually commits.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Now it’s safe to commit once at row level if needed.
                            var grid = (DataGrid)sender;
                            grid.CommitEdit(DataGridEditingUnit.Row, true);

                            // Mark dirty for the edited row.
                            if (e.Row?.Item is ItemVariantRow r)
                                MarkDirtyForCurrentRow(); // your existing comparer logic

                            UpdateSaveButtonVisibility();
                        }
                        finally
                        {
                            _squelchGridEdits = false;
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch
                {
                    _squelchGridEdits = false;
                    throw;
                }
            }
        }

        private void VariantsGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            if (_squelchGridEdits) return;

            // Don’t force-commit here either; just check dirtiness after WPF updates bindings.
            _squelchGridEdits = true;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MarkDirtyForCurrentRow();
                        UpdateSaveButtonVisibility();
                    }
                    finally
                    {
                        _squelchGridEdits = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch
            {
                _squelchGridEdits = false;
                throw;
            }
        }


        private void MarkDirtyForCurrentRow()
        {
            var row = VariantsGrid.SelectedItem as ItemVariantRow;
            if (row is null) { UpdateSaveButtonVisibility(); return; }

            if (_mode == RightMode.Standalone)
            {
                if (_standaloneOriginal is null)
                {
                    _standaloneDirty = false;
                }
                else
                {
                    _standaloneDirty = !RowsEqual(row, _standaloneOriginal);
                }
            }
            else // Variants
            {
                if (row.Id <= 0) return; // safety
                if (_originals.TryGetValue(row.Id, out var orig))
                {
                    bool dirty = !RowsEqual(row, orig);
                    if (dirty) _dirtyIds.Add(row.Id);
                    else _dirtyIds.Remove(row.Id);
                }
            }

            UpdateSaveButtonVisibility();
        }
                     
        public class NullToBoolConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type t, object p, CultureInfo c) => value != null;
            public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
        }
        public class GreaterThanZeroConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type t, object p, CultureInfo c)
                => value is int i && i > 0;
            public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
        }

        private void VariantsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private async void EditVariant_Click(object sender, RoutedEventArgs e)
        {
            if (_mode != RightMode.Variants)
            {
                MessageBox.Show("Switch to Products tab to edit a variant.");
                return;
            }
            var row = SelectedRow;
            if (row == null || row.Id <= 0)
            {
                MessageBox.Show("Select a variant first.");
                return;
            }
            await OpenDbAsync();
            var entity = await _db.Items
                .Include(i => i.Barcodes)
                .FirstOrDefaultAsync(i => i.Id == row.Id);
            if (entity == null)
            {
                MessageBox.Show("Could not load the selected variant.");
                return;
            }
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.EditSingle)
            {
                //Owner = this
            };
            var parentProduct = _selectedProduct ?? await _db.Products.FirstOrDefaultAsync(p => p.Id == entity.ProductId);
            if (parentProduct != null) dlg.PrefillProduct(parentProduct);
            dlg.PrefillForEdit(entity);                 // fills price/tax/discount/name/sku/primary etc.
            dlg.PrefillBarcodesForEdit(entity.Barcodes); // NEW: full barcode list with primary flags
            if (dlg.ShowDialog() != true) return;
            var edited = dlg.CreatedItems?.FirstOrDefault();
            if (edited == null)
            {
                MessageBox.Show("No changes returned.");
                return;
            }

            try
            {
                await OpenDbAsync();
                var dbItem = await _db.Items.Include(i => i.Barcodes).FirstAsync(i => i.Id == entity.Id);
                var newCodes = edited.Barcodes?.Select(b => b.Code.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
                if (newCodes.Count != (edited.Barcodes?.Count ?? 0))
                    throw new InvalidOperationException("Duplicate barcodes detected in the edited list.");
                bool conflict = await _db.ItemBarcodes.AnyAsync(b => newCodes.Contains(b.Code) && b.ItemId != dbItem.Id);
                if (conflict) throw new InvalidOperationException("One or more barcodes are already used by another item.");
                dbItem.Sku = edited.Sku ?? "";
                dbItem.Name = string.IsNullOrWhiteSpace(edited.Name) ? dbItem.Name : edited.Name;
                dbItem.Price = edited.Price;
                dbItem.TaxCode = edited.TaxCode;
                dbItem.DefaultTaxRatePct = edited.DefaultTaxRatePct;
                dbItem.TaxInclusive = edited.TaxInclusive;
                dbItem.DefaultDiscountPct = edited.DefaultDiscountPct;
                dbItem.DefaultDiscountAmt = edited.DefaultDiscountAmt;
                dbItem.Variant1Name = edited.Variant1Name;
                dbItem.Variant1Value = edited.Variant1Value;
                dbItem.Variant2Name = edited.Variant2Name;
                dbItem.Variant2Value = edited.Variant2Value;
                dbItem.IsActive = edited.IsActive;
                dbItem.UpdatedAt = DateTime.UtcNow;
                _db.ItemBarcodes.RemoveRange(dbItem.Barcodes);
                dbItem.Barcodes.Clear();
                foreach (var b in edited.Barcodes ?? Enumerable.Empty<ItemBarcode>())
                {
                    dbItem.Barcodes.Add(new ItemBarcode
                    {
                        ItemId = dbItem.Id,
                        Code = b.Code.Trim(),
                        Symbology = b.Symbology,
                        QuantityPerScan = Math.Max(1, b.QuantityPerScan),
                        IsPrimary = b.IsPrimary,
                        Label = string.IsNullOrWhiteSpace(b.Label) ? null : b.Label,
                        CreatedAt = b.CreatedAt == default ? DateTime.UtcNow : b.CreatedAt,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                if (dbItem.Barcodes.Any() && !dbItem.Barcodes.Any(x => x.IsPrimary))
                {
                    var firstBarcode = dbItem.Barcodes.FirstOrDefault();
                    if (firstBarcode != null) firstBarcode.IsPrimary = true;
                }
                await _db.SaveChangesAsync();
                var primary = dbItem.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
           ?? dbItem.Barcodes?.FirstOrDefault()?.Code
           ?? "";
                var idx = _gridItems.IndexOf(row);
                if (idx >= 0)
                {
                    var updatedRow = new ItemVariantRow
                    {
                        Id = dbItem.Id,
                        Sku = dbItem.Sku,
                        Name = dbItem.Name,
                        ProductName = row.ProductName, // keep same parent display
                        Barcode = primary,
                        Price = dbItem.Price,
                        Variant1Name = dbItem.Variant1Name,
                        Variant1Value = dbItem.Variant1Value,
                        Variant2Name = dbItem.Variant2Name,
                        Variant2Value = dbItem.Variant2Value,
                        TaxCode = dbItem.TaxCode,
                        DefaultTaxRatePct = dbItem.DefaultTaxRatePct,
                        TaxInclusive = dbItem.TaxInclusive,
                        DefaultDiscountPct = dbItem.DefaultDiscountPct,
                        DefaultDiscountAmt = dbItem.DefaultDiscountAmt,
                        UpdatedAt = dbItem.UpdatedAt,
                        IsActive = dbItem.IsActive,
                        IsVoided = dbItem.IsVoided
                    };
                    _gridItems[idx] = updatedRow;   // swap object (works with init-only)
                }

                MessageBox.Show("Variant updated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Update failed: " + ex.Message);
            }
        }

        private void VariantsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column == ColBarcode)
            {
                e.Cancel = true; // enforce editing barcodes only via the dedicated dialog
            }
        }

        private static ItemVariantRow CloneRow(ItemVariantRow r) => new()
        {
            Id = r.Id,
            Sku = r.Sku,
            Name = r.Name,
            ProductName = r.ProductName,
            Barcode = r.Barcode,
            Price = r.Price,
            Variant1Name = r.Variant1Name,
            Variant1Value = r.Variant1Value,
            Variant2Name = r.Variant2Name,
            Variant2Value = r.Variant2Value,
            TaxCode = r.TaxCode,
            DefaultTaxRatePct = r.DefaultTaxRatePct,
            TaxInclusive = r.TaxInclusive,
            DefaultDiscountPct = r.DefaultDiscountPct,
            DefaultDiscountAmt = r.DefaultDiscountAmt,
            BrandId = r.BrandId,
            CategoryId = r.CategoryId,
            UpdatedAt = r.UpdatedAt,
            IsActive = r.IsActive,
            IsVoided = r.IsVoided
        };

        private static bool RowsEqual(ItemVariantRow a, ItemVariantRow b)
        {
            if (a is null || b is null) return false;
            return
                string.Equals(a.Sku, b.Sku, StringComparison.Ordinal) &&
                string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&            // standalone visible, variants hidden
                a.Price == b.Price &&
                string.Equals(a.Variant1Name, b.Variant1Name, StringComparison.Ordinal) &&
                string.Equals(a.Variant1Value, b.Variant1Value, StringComparison.Ordinal) &&
                string.Equals(a.Variant2Name, b.Variant2Name, StringComparison.Ordinal) &&
                string.Equals(a.Variant2Value, b.Variant2Value, StringComparison.Ordinal) &&
                string.Equals(a.TaxCode, b.TaxCode, StringComparison.Ordinal) &&
                a.DefaultTaxRatePct == b.DefaultTaxRatePct &&
                a.TaxInclusive == b.TaxInclusive &&
                a.DefaultDiscountPct == b.DefaultDiscountPct &&
                a.DefaultDiscountAmt == b.DefaultDiscountAmt &&
                a.BrandId == b.BrandId &&
                a.CategoryId == b.CategoryId &&
                a.IsActive == b.IsActive
                // Barcode intentionally excluded — locked & edited elsewhere
                ;
        }

        private void UpdateSaveButtonVisibility()
        {
            bool show = (_mode == RightMode.Variants && _dirtyIds.Count > 0)
                        || (_mode == RightMode.Standalone && _standaloneDirty);
            if (BtnSaveChanges != null)
                BtnSaveChanges.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }


    }
}

