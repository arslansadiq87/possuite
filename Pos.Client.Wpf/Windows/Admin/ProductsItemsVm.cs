// Pos.Client.Wpf/Windows/Admin/ProductsItemsVm.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pos.Domain.Entities;
using Pos.Persistence.Services;     // CatalogService
using Pos.Client.Wpf.Services;      // ThumbnailService
using Pos.Persistence.Media;


namespace Pos.Client.Wpf.Windows.Admin;

public partial class ProductsItemsVm : ObservableObject
{
    private readonly CatalogService _svc;
    private readonly ThumbnailService _thumbs = new ThumbnailService();

    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<string> DisplayGalleryThumbs { get; } = new();

    [ObservableProperty] private Item? selectedVariant;
    [ObservableProperty] private string? selectedPrimaryThumb;
    [ObservableProperty] private Product? selected;

    public ProductsItemsVm(CatalogService svc)
    {
        _svc = svc; // resolved from DI
    }

    [RelayCommand]
    public async Task SetProductPrimaryImageAsync()
    {
        if (Selected is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Choose Product Image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        MediaPaths.Ensure();

        _ = await _svc.SetProductPrimaryImageAsync(
            Selected.Id,
            dlg.FileName,
            stem => _thumbs.CreateThumb(dlg.FileName, stem));

        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == Selected!.Id);
    }


    [RelayCommand]
    public async Task AddProductGalleryImageAsync()
    {
        if (Selected is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Add Gallery Image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        MediaPaths.Ensure();

        _ = await _svc.AddProductGalleryImageAsync(
            Selected.Id,
            dlg.FileName,
            stem => _thumbs.CreateThumb(dlg.FileName, stem));

        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == Selected!.Id);
    }


    [RelayCommand]
    public async Task SetVariantPrimaryImageAsync(Item? variant)
    {
        if (Selected is null || variant is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Choose Variant Image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        MediaPaths.Ensure();

        _ = await _svc.SetItemPrimaryImageAsync(
            variant.Id,
            dlg.FileName,
            stem => _thumbs.CreateThumb(dlg.FileName, stem));

        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == Selected!.Id);
    }



    [RelayCommand]
    public async Task LoadAsync()
    {
        Products.Clear();
        var list = await _svc.GetProductsForVmAsync(); // includes Brand, Category, Variants + Barcodes
        foreach (var p in list) Products.Add(p);
    }


    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Selected is null) return;

        if (Selected.Id == 0)
        {
            var created = await _svc.CreateProductAsync(
                name: Selected.Name ?? "",
                brandId: Selected.BrandId,
                categoryId: Selected.CategoryId);
            Selected = created;
        }
        else
        {
            var updated = await _svc.UpdateProductAsync(
                productId: Selected.Id,
                name: Selected.Name ?? "",
                brandId: Selected.BrandId,
                categoryId: Selected.CategoryId);
            Selected = updated;
        }

        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == Selected?.Id);
    }


    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Selected is null || Selected.Id == 0) return;

        var (canDelete, reason) = await _svc.CanHardDeleteProductAsync(Selected.Id);
        if (!canDelete)
        {
            var res = System.Windows.MessageBox.Show(
                $"{reason}\n\nDo you want to void this product instead?",
                "Cannot delete product",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (res == System.Windows.MessageBoxResult.Yes)
                await _svc.VoidProductAsync(Selected.Id, System.Environment.UserName);
        }
        else
        {
            await _svc.DeleteProductAsync(Selected.Id);
        }

        await LoadAsync();
        Selected = null;
    }


    [RelayCommand]
    public void NewProduct()
    {
        Selected = new Product { Name = "New Product", IsActive = true };
        Products.Add(Selected);
    }


    /// <summary>
    /// Add already-constructed item variants to a product (sync-aware).
    /// Items should already include Barcodes list, pricing/tax fields, etc.
    /// </summary>
    public async Task AddVariantsAsync(Product product, IEnumerable<Item> items)
    {
        if (product is null) return;

        Product persisted;
        if (product.Id == 0)
            persisted = await _svc.CreateProductAsync(product.Name ?? "", product.BrandId, product.CategoryId);
        else
            persisted = await _svc.UpdateProductAsync(product.Id, product.Name ?? "", product.BrandId, product.CategoryId);

        var allCodes = items
            .SelectMany(it => it.Barcodes ?? Enumerable.Empty<ItemBarcode>())
            .Select(b => b.Code)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allCodes.Count > 0)
        {
            var conflicts = await _svc.FindBarcodeConflictsAsync(allCodes, excludeItemId: null);
            if (conflicts.Count > 0)
            {
                var lines = conflicts
                    .GroupBy(c => c.Code)
                    .Select(g =>
                    {
                        var x = g.First();
                        var owner = !string.IsNullOrWhiteSpace(x.ProductName)
                            ? $"Product: {x.ProductName}, Variant: {x.ItemName}"
                            : $"Item: {x.ItemName}";
                        return $"• {g.Key} → already used by {owner}";
                    });
                System.Windows.MessageBox.Show(
                    "One or more barcodes are already in use:\n\n" +
                    string.Join("\n", lines) +
                    "\n\nPlease change these barcodes.",
                    "Duplicate barcode(s) found",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        foreach (var it in items)
        {
            it.ProductId = persisted.Id;
            it.Product = null;
            it.UpdatedAt = DateTime.UtcNow;
            await _svc.CreateItemAsync(it);
        }

        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == persisted.Id);
    }

}
