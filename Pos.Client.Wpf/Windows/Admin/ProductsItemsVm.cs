//Pos.Client.Wpf/windows/Admin/ProductsItemsVm
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin;

public partial class ProductsItemsVm : ObservableObject
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    public ObservableCollection<Product> Products { get; } = new();

    [ObservableProperty] private Product? selected;

    public ProductsItemsVm(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    [RelayCommand]
    public async Task LoadAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();

        Products.Clear();
        // NOTE: your Product has Variants (not Items). If you rename to Items later, change here too.
        var list = await db.Products
            .Include(p => p.Variants)
            .OrderBy(p => p.Name)
            .ToListAsync();

        foreach (var p in list) Products.Add(p);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Selected is null) return;
        using var db = await _dbf.CreateDbContextAsync();

        db.Attach(Selected).State = Selected.Id == 0 ? EntityState.Added : EntityState.Modified;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Selected is null || Selected.Id == 0) return;
        using var db = await _dbf.CreateDbContextAsync();

        db.Remove(Selected);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    public void NewProduct()
    {
        Selected = new Product { Name = "New Product", IsActive = true };
        Products.Add(Selected);
    }
    public async Task AddVariantsAsync(Product product, IEnumerable<Item> items)
    {
        if (product is null) return;

        await using var db = await _dbf.CreateDbContextAsync();

        // Ensure Product is tracked (save if new)
        if (product.Id == 0)
        {
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        // Attach generated items with the product id
        var now = DateTime.UtcNow;
        foreach (var it in items)
        {
            it.ProductId = product.Id;
            it.Product = null;
            it.UpdatedAt = now;
            db.Items.Add(it); // assuming DbSet<Item> Items exists
        }

        // Optional: server-side uniqueness checks can still fail; catching here for UX
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // TODO: inspect inner exception for unique index violations on SKU/Barcode
            System.Windows.MessageBox.Show("Failed to save one or more variants (possible duplicate SKU/Barcode). " +
                                           "Details: " + ex.Message);
        }

        // Refresh the selected product’s variants in the UI
        await LoadAsync();
        Selected = Products.FirstOrDefault(p => p.Id == product.Id);
    }
}
