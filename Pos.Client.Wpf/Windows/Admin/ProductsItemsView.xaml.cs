// Pos.Client.Wpf/Windows/Admin/ProductsItemsWindow.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Domain.Models;
using System.Windows.Input;
using System.Globalization;
using Pos.Client.Wpf.Services;   // ThumbnailService
using Pos.Domain.Services;
using System.IO;
using Microsoft.Win32;


namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ProductsItemsView : UserControl
    {
        private readonly ICatalogService _svc;
        private readonly ThumbnailService _thumbs = new(); // already present below, keep single instance
        private bool _squelchGridEdits;
        private readonly Dictionary<int, ItemVariantRow> _originals = new(); // for variants list
        private readonly HashSet<int> _dirtyIds = new();                      // changed variant rows
        private ItemVariantRow? _standaloneOriginal;                          // snapshot of selected standalone
        private bool _standaloneDirty;                                        // flag for standalone
        private readonly ObservableCollection<string> _galleryThumbs = new();
        public ObservableCollection<string> GalleryThumbs => _galleryThumbs;

        private readonly ObservableCollection<Product> _products = new();
        private readonly ObservableCollection<ItemVariantRow> _standalone = new();
        private readonly ObservableCollection<ItemVariantRow> _gridItems = new();
        private Product? _selectedProduct;
        private bool _loadingProducts;
        private bool _loadingVariants;
        private bool _loadingStandalone;
        private bool _saving;
        // 1) Add this field near the other private fields
        private int? _pendingSelectStandaloneId;

        private enum RightMode { Variants, Standalone }
        private RightMode _mode;
        private readonly ObservableCollection<Brand> _brands = new();
        private readonly ObservableCollection<Category> _categories = new();
        public ObservableCollection<Brand> Brands => _brands;
        public ObservableCollection<Category> Categories => _categories;
        private ItemVariantRow? SelectedRow => VariantsGrid.SelectedItem as ItemVariantRow;

        public ProductsItemsView()
        {
            InitializeComponent();
            DataContext = this;
            _svc = App.Services.GetRequiredService<ICatalogService>();
            ProductsList.ItemsSource = _products;
            StandaloneList.ItemsSource = _standalone;
            VariantsGrid.ItemsSource = _gridItems;
            Loaded += async (_, __) =>
            {
                _brands.Clear();
                foreach (var b in await _svc.GetActiveBrandsAsync())   // ← add this to service if missing
                    _brands.Add(b);
                _categories.Clear();
                foreach (var c in await _svc.GetActiveCategoriesAsync()) // ← add this to service if missing
                    _categories.Add(c);
                LeftTabs.SelectedIndex = 0; // default: Products
                await RefreshAsync();
            };
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
            ColBrand.Visibility = Visibility.Collapsed;
            ColCategory.Visibility = Visibility.Collapsed;
            if (BtnAddVariant != null) BtnAddVariant.Visibility = isVariants ? Visibility.Visible : Visibility.Collapsed;
            if (BtnDeleteVoidItem != null) BtnDeleteVoidItem.Tag = isVariants;
            if (BtnEditVariant != null) BtnEditVariant.Tag = isVariants;
            if (BtnDeleteVoidVariant != null) BtnDeleteVoidVariant.Tag = isVariants;
            if (ColBarcode != null) ColBarcode.IsReadOnly = true;
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
                var term = SearchBox.Text?.Trim() ?? "";
                var list = await _svc.SearchProductsAsync(term);
                int? selectedId = (ProductsList.SelectedItem as Product)?.Id;
                _products.Clear();
                foreach (var p in list) _products.Add(p);
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
                var term = SearchBox.Text?.Trim() ?? "";
                var rows = await _svc.SearchStandaloneItemRowsAsync(term);
                int? selectedId = (StandaloneList.SelectedItem as ItemVariantRow)?.Id;
                _standalone.Clear();
                foreach (var r in rows) _standalone.Add(r);
                ItemVariantRow? restored = null;
                if (selectedId is int id)
                    restored = _standalone.FirstOrDefault(x => x.Id == id);
                if (restored != null)
                {
                    StandaloneList.SelectedItem = restored;
                    StandaloneList.UpdateLayout();
                    StandaloneList.ScrollIntoView(restored);
                    _gridItems.Clear();
                    _gridItems.Add(restored);
                    GridTitle.Text = $"Standalone Item — {restored.Name}";
                    _standaloneOriginal = CloneRow(restored);
                    _standaloneDirty = false;
                    await LoadGalleryForVariantAsync(restored.Id);
                }
                else
                {
                    StandaloneList.SelectedItem = null;
                    _gridItems.Clear();
                    GridTitle.Text = "Standalone Items — (no selection)";
                    _standaloneOriginal = null;
                    _standaloneDirty = false;
                    GalleryThumbs.Clear();
                }

                SetRightGridMode(RightMode.Standalone);
                _originals.Clear();
                _dirtyIds.Clear();
                _standaloneDirty = false;
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
                if (product?.Id > 0)
                    await LoadGalleryForProductAsync(product.Id);
                else
                    GalleryThumbs.Clear();
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
            if (ProductsList.SelectedItem is Product pSel)
                await LoadGalleryForProductAsync(pSel.Id);
            else
                GalleryThumbs.Clear();
        }

        private async void NewProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe) fe.IsEnabled = false;
            try
            {
                var dlg = new ProductNameDialog();
                if (dlg.ShowDialog() != true) return;
                var p = await _svc.CreateProductAsync(dlg.ProductName, dlg.BrandId, dlg.CategoryId);
                try
                {
                    //MediaPaths.Ensure();
                    if (!string.IsNullOrWhiteSpace(dlg.PrimaryImagePath))
                    {
                        var prim = dlg.PrimaryImagePath!;
                        await _svc.SetProductPrimaryImageAsync(
                            p.Id,
                            prim,
                            stem => _thumbs.CreateThumb(prim, stem));
                    }

                    if (dlg.GalleryImagePaths.Count > 0)
                    {
                        foreach (var g in dlg.GalleryImagePaths)
                        {
                            await _svc.AddProductGalleryImageAsync(
                                p.Id,
                                g,
                                stem => _thumbs.CreateThumb(g, stem));
                        }
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Product name must be unique", StringComparison.OrdinalIgnoreCase))
                {
                    // Show friendly message instead of crashing
                    MessageBox.Show("Product name must be unique. Please choose a different name.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Product images could not be saved: " + ex.Message);
                }
                _products.Insert(0, p);
                ProductsList.SelectedItem = p;
            }
            finally
            {
                if (sender is FrameworkElement fe2) fe2.IsEnabled = true;
            }
        }

        private async void EditProduct_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsList.SelectedItem is not Product p)
            {
                MessageBox.Show("Select a product first.");
                return;
            }
            var dlg = new ProductNameDialog { };
            dlg.Prefill(p.Name, p.BrandId, p.CategoryId);
            if (dlg.ShowDialog() != true) return;
            var updated = await _svc.UpdateProductAsync(p.Id, dlg.ProductName, dlg.BrandId, dlg.CategoryId);
            try
            {
                //MediaPaths.Ensure();
                if (dlg.PrimaryImagePath is string prim)
                {
                    await _svc.SetProductPrimaryImageAsync(
                        p.Id,
                        prim,
                        stem => _thumbs.CreateThumb(prim, stem));
                }
                if (dlg.GalleryImagePaths.Count > 0)
                {
                    await _svc.ClearProductGalleryImagesAsync(p.Id);
                    foreach (var g in dlg.GalleryImagePaths)
                    {
                        await _svc.AddProductGalleryImageAsync(
                            p.Id,
                            g,
                            stem => _thumbs.CreateThumb(g, stem));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Product images could not be saved: " + ex.Message);
            }

            var inList = _products.FirstOrDefault(x => x.Id == p.Id);
            if (inList != null)
            {
                inList.Name = updated.Name;
                inList.BrandId = updated.BrandId;
                inList.CategoryId = updated.CategoryId;
                inList.Brand = updated.Brand;
                inList.Category = updated.Category;
            }

            if (_selectedProduct?.Id == p.Id)
                GridTitle.Text = $"Variants — {updated.Name}";
        }

        private async void AddVariants_Click(object sender, RoutedEventArgs e)
        {
            var product = _selectedProduct ?? ProductsList.SelectedItem as Product;
            if (product == null) { MessageBox.Show("Select a product first."); return; }
            GridTitle.Text = $"Variants — {product.Name}";
            SetRightGridMode(RightMode.Variants);
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.Sequential);
            dlg.SaveImmediately = true; // makes "Save & Add another" call the saver below
            dlg.SaveOneAsync = async (item) =>
            {
                try
                {
                    var saved = await _svc.CreateItemAsync(item);
                    await ApplyPendingVariantImagesAsync(dlg, saved.Id);
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
            };
            dlg.PrefillProduct(product);
            if (dlg.ShowDialog() != true) return;
            if (dlg.CreatedItems is { Count: > 0 })
            {
                foreach (var v in dlg.CreatedItems)
                {
                    var saved = await _svc.CreateItemAsync(v);
                    await ApplyPendingVariantImagesAsync(dlg, saved.Id);
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
            // ⬇️ Always reload variants from DB after dialog completes (covers both paths)
            await LoadVariantsForProduct(product);
            VariantsGrid?.Items?.Refresh(); // optional; binding usually handles it

        }

        private async void NewStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            _pendingSelectStandaloneId = null; // reset before opening the dialog

            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.Sequential);
            dlg.SaveImmediately = true;
            dlg.SaveOneAsync = async (item) =>
            {
                try
                {
                    var saved = await _svc.CreateItemAsync(item);
                    await ApplyPendingVariantImagesAsync(dlg, saved.Id);
                    _pendingSelectStandaloneId = saved.Id;

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
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Save failed: " + ex.Message);
                    return false;
                }
            };
            dlg.PrefillStandalone();
            // was: if (dlg.ShowDialog() == true && dlg.CreatedItems?.Count > 0)
            if (dlg.ShowDialog() == true)
            {
                // If batch path was used, persist them now and remember the last id
                if (dlg.CreatedItems?.Count > 0)
                {
                    int? lastId = null;
                    foreach (var v in dlg.CreatedItems)
                    {
                        var saved = await _svc.CreateItemAsync(v);
                        await ApplyPendingVariantImagesAsync(dlg, saved.Id);
                        lastId = saved.Id;

                        // (optional UX) keep this if you like instant visual feedback before reload
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
                    _pendingSelectStandaloneId = lastId;
                }

                // 🔑 Always refresh after dialog close (covers SaveImmediately path too)
                await RefreshStandaloneAsync();

                // Try to re-select the newly created item if we know it
                if (_pendingSelectStandaloneId is int id)
                {
                    var restored = _standalone.FirstOrDefault(x => x.Id == id);

                    // If a search term hid it, temporarily clear filter, reload, reselect, then restore term
                    if (restored == null && !string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        var old = SearchBox.Text;
                        SearchBox.Text = "";
                        await RefreshStandaloneAsync();
                        restored = _standalone.FirstOrDefault(x => x.Id == id);
                        SearchBox.Text = old; // restore
                    }

                    if (restored != null)
                    {
                        StandaloneList.SelectedItem = restored;
                        StandaloneList.UpdateLayout();
                        StandaloneList.ScrollIntoView(restored);

                        _gridItems.Clear();
                        _gridItems.Add(restored);
                        GridTitle.Text = $"Standalone Item — {restored.Name}";
                        await LoadGalleryForVariantAsync(restored.Id);
                    }
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
                IEnumerable<ItemVariantRow> toSave;
                if (_mode == RightMode.Standalone)
                {
                    if (!_standaloneDirty) { MessageBox.Show("No changes to save."); return; }
                    toSave = _gridItems.ToList(); // only one row
                }
                else
                {
                    if (_dirtyIds.Count == 0) { MessageBox.Show("No changes to save."); return; }
                    var set = _dirtyIds.ToHashSet();
                    toSave = _gridItems.Where(r => set.Contains(r.Id)).ToList();
                }
                var savedRows = await _svc.SaveVariantRowsAsync(toSave);
                if (_mode == RightMode.Standalone)
                {
                    var fresh = savedRows.FirstOrDefault();
                    if (fresh != null)
                    {
                        _gridItems[0] = fresh;
                        _standaloneOriginal = CloneRow(fresh);
                        _standaloneDirty = false;
                    }
                }
                else
                {
                    var map = savedRows.ToDictionary(r => r.Id);
                    for (int i = 0; i < _gridItems.Count; i++)
                    {
                        var r = _gridItems[i];
                        if (map.TryGetValue(r.Id, out var fresh))
                        {
                            _gridItems[i] = fresh;
                            _originals[fresh.Id] = CloneRow(fresh);
                            _dirtyIds.Remove(fresh.Id);
                        }
                    }
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
            var entity = await _svc.GetItemWithBarcodesAsync(row.Id);
            if (entity == null)
            {
                MessageBox.Show("Could not load the selected item.");
                return;
            }
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.EditSingle) { };
            await dlg.PrefillStandaloneForEditAsync(entity);
            if (dlg.ShowDialog() != true) return;
            try
            {
                var edited = dlg.CreatedItems?.FirstOrDefault();
                if (edited == null) { MessageBox.Show("No changes returned."); return; }
                var freshRow = await _svc.EditSingleItemAsync(edited);
                _gridItems.Clear();
                _gridItems.Add(freshRow);
                GridTitle.Text = $"Standalone Item — {freshRow.Name}";
                _standaloneOriginal = CloneRow(freshRow);
                _standaloneDirty = false;
                await ApplyPendingVariantImagesAsync(dlg, freshRow.Id);
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

        private async void DeleteOrVoidStandalone_Click(object sender, RoutedEventArgs e)
        {
            // Must act on LEFT list selection (standalone item)
            var row = StandaloneList?.SelectedItem as ItemVariantRow;
            if (row == null || row.Id <= 0 || row.ProductName != null)
            {
                MessageBox.Show("Pick a standalone item from the left list.");
                return;
            }

            // Ask the service if hard delete is allowed
            var (canDelete, reason) = await _svc.CanHardDeleteItemAsync(row.Id);

            if (canDelete)
            {
                // Offer Delete (Yes) or Void (No). Cancel does nothing.
                var choice = MessageBox.Show(
                    $"What do you want to do with '{row.Name}'?\n\n" +
                    "Yes = Delete permanently\nNo = Void (disable)\nCancel = Do nothing",
                    "Delete or Void item",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Yes)
                {
                    // Confirm destructive delete
                    if (MessageBox.Show(
                            $"Delete item '{row.Name}' permanently?",
                            "Confirm Delete",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;

                    await _svc.DeleteItemAsync(row.Id);
                    await RefreshAsync();
                    MessageBox.Show("Item deleted.");
                }
                else if (choice == MessageBoxResult.No)
                {
                    await _svc.VoidItemAsync(row.Id, user: "admin");
                    await RefreshAsync();
                    MessageBox.Show("Item voided.");
                }
                // Cancel → do nothing
            }
            else
            {
                // Deletion not allowed: explain and offer Void
                var choice = MessageBox.Show(
                    $"This item can’t be deleted.\nReason: {reason}\n\n" +
                    "Do you want to Void it instead?",
                    "Void item",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (choice == MessageBoxResult.Yes)
                {
                    await _svc.VoidItemAsync(row.Id, user: "admin");
                    await RefreshAsync();
                    MessageBox.Show("Item voided.");
                }
            }
        }

        // REPLACE the body of DeleteStandaloneItem_Click with this:
        private async void DeleteStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            // Prefer left list selection in Standalone mode
            var row = _mode == RightMode.Standalone
                ? StandaloneList.SelectedItem as ItemVariantRow
                : SelectedRow;

            if (row == null || row.Id <= 0 || row.ProductName != null)
            {
                MessageBox.Show("Pick a standalone item from the left list.");
                return;
            }

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


        // REPLACE the body of VoidStandaloneItem_Click with this:
        private async void VoidStandaloneItem_Click(object sender, RoutedEventArgs e)
        {
            var row = _mode == RightMode.Standalone
                ? StandaloneList.SelectedItem as ItemVariantRow
                : SelectedRow;

            if (row == null || row.Id <= 0 || row.ProductName != null)
            {
                MessageBox.Show("Pick a standalone item from the left list.");
                return;
            }

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
                // NEW: show its images immediately
                if (row.Id > 0)
                    await LoadGalleryForVariantAsync(row.Id);
                else
                    GalleryThumbs.Clear();
            }
            else
            {
                GridTitle.Text = "Standalone Items — (no selection)";
                _standaloneOriginal = null;
                _standaloneDirty = false;
                GalleryThumbs.Clear(); // NEW: ensure strip hides when nothing selected

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
                _squelchGridEdits = true;
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var grid = (DataGrid)sender;
                            grid.CommitEdit(DataGridEditingUnit.Row, true);
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

        void MarkDirtyForCurrentRow()
        {
            // Use the correct selection source based on current mode
            var row = _mode == RightMode.Standalone
                ? StandaloneList.SelectedItem as ItemVariantRow
                : VariantsGrid.SelectedItem as ItemVariantRow;

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
                if (row.Id <= 0) { UpdateSaveButtonVisibility(); return; }
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

        private async void VariantsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mode == RightMode.Variants)
            {
                var row = SelectedRow;
                if (row?.Id > 0)
                {
                    await LoadGalleryForVariantAsync(row.Id);
                    return;
                }
                if (_selectedProduct?.Id > 0)
                {
                    await LoadGalleryForProductAsync(_selectedProduct.Id);
                }
                else
                {
                    GalleryThumbs.Clear();
                }
            }
            else // Standalone mode
            {
                var row = SelectedRow;
                if (row?.Id > 0)
                    await LoadGalleryForVariantAsync(row.Id);
                else
                    GalleryThumbs.Clear();
            }
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

            var entity = await _svc.GetItemWithBarcodesAsync(row.Id);
            if (entity == null)
            {
                MessageBox.Show("Could not load the selected variant.");
                return;
            }
            var dlg = new VariantBatchDialog(VariantBatchDialog.Mode.EditSingle) { };
            var parentProduct = _selectedProduct;
            if (parentProduct == null && entity.ProductId is int pid)
                parentProduct = await _svc.GetProductAsync(pid);
            if (parentProduct != null) dlg.PrefillProduct(parentProduct);
            dlg.PrefillForEdit(entity);
            dlg.PrefillBarcodesForEdit(entity.Barcodes);
            if (dlg.ShowDialog() != true) return;
            try
            {
                var edited = dlg.CreatedItems?.FirstOrDefault();
                if (edited == null) { MessageBox.Show("No changes returned."); return; }
                var freshRow = await _svc.EditSingleItemAsync(edited);
                await ApplyPendingVariantImagesAsync(dlg, freshRow.Id);
                var idx = _gridItems.IndexOf(row);
                if (idx >= 0) _gridItems[idx] = freshRow;
                MessageBox.Show("Variant updated.");
                // ⬇️ Ensure grid shows authoritative state from DB
                if (_selectedProduct != null)
                {
                    await LoadVariantsForProduct(_selectedProduct);
                    VariantsGrid?.Items?.Refresh();
                }

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
                ;
        }

        private void UpdateSaveButtonVisibility()
        {
            bool show = (_mode == RightMode.Variants && _dirtyIds.Count > 0)
                        || (_mode == RightMode.Standalone && _standaloneDirty);
            if (BtnSaveChanges != null)
                BtnSaveChanges.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadGalleryForProductAsync(int productId)
        {
            _galleryThumbs.Clear();
            var thumbs = await _svc.GetAllThumbsForProductAsync(productId);
            foreach (var t in thumbs) _galleryThumbs.Add(t);
        }

        private async Task LoadGalleryForVariantAsync(int itemId)
        {
            var vThumbs = await _svc.GetItemThumbsAsync(itemId);
            if (vThumbs.Count > 0)
            {
                _galleryThumbs.Clear();
                foreach (var t in vThumbs) _galleryThumbs.Add(t);
                return;
            }
            var pid = await _svc.GetProductIdForItemAsync(itemId);
            if (pid is int productId) await LoadGalleryForProductAsync(productId);
            else _galleryThumbs.Clear();
        }


        private async Task ApplyPendingVariantImagesAsync(VariantBatchDialog dlg, int itemId)
        {
            var pack = dlg.PopPendingVariantImages();   // returns (PrimaryPath, GalleryPaths)
            if (pack is null) return;
            try
            {
                //MediaPaths.Ensure();
                if (pack.PrimaryPath is string prim)
                {
                    await _svc.SetItemPrimaryImageAsync(
                        itemId,
                        prim,
                        stem => _thumbs.CreateThumb(prim, stem));
                }
                foreach (var g in pack.GalleryPaths)
                {
                    await _svc.AddItemGalleryImageAsync(
                        itemId,
                        g,
                        stem => _thumbs.CreateThumb(g, stem));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Variant images could not be saved: " + ex.Message);
            }
        }

        private void DownloadSampleCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "pos-catalog-sample.csv"
            };
            if (sfd.ShowDialog() == true)
            {
                // Embedded sample (same headers used by parser)
                var sample = string.Join("\r\n", new[]
                {
            "Type,ProductName,ItemName,SKU,Barcode,Price,TaxCode,TaxRatePct,TaxInclusive,Brand,Category,Variant1Name,Variant1Value,Variant2Name,Variant2Value",
            "Standalone,,USB-C Cable 1m,USB-C-1M,1234567890123,499,STD,18,false,WirePro,Cables,,,,",
            "Standalone,,AA Alkaline Battery 4-Pack,AA-ALK-4,9901234567890,299,RED,8,true,PowerMax,Batteries,,,,",
            "Variant,T-Shirt Classic,T-Shirt Classic Blue S,TS-CL-BLU-S,1112223334445,1499,STD,18,false,UrbanWear,Apparel,Color,Blue,Size,S",
            "Variant,T-Shirt Classic,T-Shirt Classic Blue M,TS-CL-BLU-M,1112223334446,1499,STD,18,false,UrbanWear,Apparel,Color,Blue,Size,M"
        });
                File.WriteAllText(sfd.FileName, sample);
                MessageBox.Show("Sample saved.");
            }
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var win = new Window
            {
                Title = "Import Products & Items (CSV)",
                Content = new ImportCatalogCsvDialog(),
                Width = 1200,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };
            win.ShowDialog();
            // Optional: refresh lists after closing
            _ = RefreshAsync();
        }
    }
}

