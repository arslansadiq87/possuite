using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models;                 // ItemVariantRow (you already use this)
using Pos.Domain.Models.Catalog;        // BarcodeConflict (new DTO below)

namespace Pos.Domain.Services
{
    public interface ICatalogService
    {
        // Images (write)
        Task<ProductImage> SetProductPrimaryImageAsync(int productId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default);
        Task ClearProductGalleryImagesAsync(int productId, CancellationToken ct = default);
        Task<ItemImage> SetItemPrimaryImageAsync(int itemId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default);
        Task<ProductImage> AddProductGalleryImageAsync(int productId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default);
        Task<ItemImage> AddItemGalleryImageAsync(int itemId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default);
        Task DeleteImageAsync(string kind, int imageId, bool deleteFiles = false, CancellationToken ct = default);

        // Products
        Task<List<Product>> SearchProductsAsync(string? term, int take = 100, CancellationToken ct = default);
        Task<Product> CreateProductAsync(string name, int? brandId = null, int? categoryId = null, CancellationToken ct = default);
        Task<Product?> GetProductAsync(int productId, CancellationToken ct = default);
        Task<Product> UpdateProductAsync(int productId, string name, int? brandId, int? categoryId, CancellationToken ct = default);
        Task<(bool canDelete, string? reason)> CanHardDeleteProductAsync(int productId, CancellationToken ct = default);
        Task DeleteProductAsync(int productId, CancellationToken ct = default);
        Task VoidProductAsync(int productId, string user, CancellationToken ct = default);

        // Items / Variants (reads for UI)
        Task<List<ItemVariantRow>> GetItemsForProductAsync(int productId, CancellationToken ct = default);
        Task<List<ItemVariantRow>> SearchStandaloneItemRowsAsync(string term, CancellationToken ct = default);
        Task<List<Item>> SearchStandaloneItemsAsync(string? term, int take = 200, CancellationToken ct = default);

        // Items / Variants (writes)
        Task<Item> CreateItemAsync(Item i, CancellationToken ct = default);
        Task<Item> UpdateItemAsync(Item updated, CancellationToken ct = default);
        Task<List<Item>> BulkCreateVariantsAsync(
            int productId,
            string itemBaseName,
            string axis1Name, IEnumerable<string> axis1Values,
            string axis2Name, IEnumerable<string> axis2Values,
            decimal price, string? taxCode, decimal taxPct, bool taxInclusive,
            decimal? defDiscPct, decimal? defDiscAmt,
            CancellationToken ct = default);

        // Barcodes
        Task<ItemBarcode> AddBarcodeAsync(int itemId, string code, BarcodeSymbology sym, int qtyPerScan = 1, bool isPrimary = false, string? label = null, CancellationToken ct = default);
        Task<(Item item, int qty)> ResolveScanAsync(string scannedCode, CancellationToken ct = default);
        Task<(bool Taken, string? ProductName, string? ItemName, int? ProductId, int ItemId)> TryGetBarcodeOwnerAsync(string code, int? excludeItemId = null, CancellationToken ct = default);
        Task<List<BarcodeConflict>> FindBarcodeConflictsAsync(IEnumerable<string> codes, int? excludeItemId = null, CancellationToken ct = default);
        Task<(string Code, int AdvancedBy)> GenerateUniqueBarcodeAsync(BarcodeSymbology sym, string prefix, int startSeq, int maxTries = 10000, CancellationToken ct = default);

        // Read helpers for UI
        Task<Item?> GetItemWithBarcodesAsync(int itemId, CancellationToken ct = default);
        Task<List<string>> GetProductThumbsAsync(int productId, CancellationToken ct = default);
        Task<List<string>> GetItemThumbsAsync(int itemId, CancellationToken ct = default);
        Task<int?> GetProductIdForItemAsync(int itemId, CancellationToken ct = default);
        Task<Item> UpdateItemFromRowAsync(ItemVariantRow r, CancellationToken ct = default);
        Task<List<ItemVariantRow>> SaveVariantRowsAsync(IEnumerable<ItemVariantRow> rows, CancellationToken ct = default);
        Task<(Item item, string? primaryCode)> ReplaceBarcodesAsync(int itemId, IEnumerable<ItemBarcode> newBarcodes, CancellationToken ct = default);
        Task<ItemVariantRow> EditSingleItemAsync(Item editedWithBarcodes, CancellationToken ct = default);
        Task<List<string>> GetAllThumbsForProductAsync(int productId, CancellationToken ct = default);

        // Lookups / VM friendly
        Task<List<Brand>> GetActiveBrandsAsync(CancellationToken ct = default);
        Task<List<Category>> GetActiveCategoriesAsync(CancellationToken ct = default);
        Task<List<Product>> GetProductsForVmAsync(CancellationToken ct = default);
        Task<List<Category>> GetAllCategoriesAsync(CancellationToken ct = default);

        // Utilities
        Task<int> GetNextSkuSequenceAsync(string prefix, int fallbackStart, CancellationToken ct = default);

        Task VoidItemAsync(int itemId, string user, CancellationToken ct = default);
        Task DeleteItemAsync(int itemId, CancellationToken ct = default);
        Task<(bool canDelete, string? reason)> CanHardDeleteItemAsync(int itemId, CancellationToken ct = default);
    }
}
