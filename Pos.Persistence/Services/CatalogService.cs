// Pos.Persistence/Services/CatalogService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models;
using Pos.Domain.Models.Catalog;
using Pos.Domain.Services;
using Pos.Domain.Utils;
using Pos.Persistence.Media;
using Pos.Persistence.Sync;
using System.Security.Cryptography;
using System.IO;
using System.Linq;
using Pos.Persistence.Outbox;

namespace Pos.Persistence.Services
{
    public sealed class CatalogService : ICatalogService, IItemsWriteService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public CatalogService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // ---------- helpers ----------
        private static string Sha1(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(s);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static void TryDelete(string? path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }

        // ---------- Images (write) ----------
        public async Task<ProductImage> SetProductPrimaryImageAsync(
            int productId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default)
        {
            EnsureMedia();
            await using var db = await _dbf.CreateDbContextAsync(ct);

            _ = await db.Products.FindAsync(new object?[] { productId }, ct)
                 ?? throw new InvalidOperationException("Product not found.");

            var ext = Path.GetExtension(originalLocalPath);
            var stem = $"p{productId}_{Guid.NewGuid():N}";
            var staged = Path.Combine(MediaPaths.OriginalsDir, stem + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(originalLocalPath, staged, overwrite: true);

            var sha1 = Sha1(staged);
            var thumb = createThumbAt(stem);

            var oldPrimaryIds = await db.ProductImages
                .Where(x => x.ProductId == productId && x.IsPrimary)
                .Select(x => x.Id)
                .ToListAsync(ct);

            await db.ProductImages
                .Where(x => x.ProductId == productId && x.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, false), ct);

            var img = new ProductImage
            {
                ProductId = productId,
                IsPrimary = true,
                SortOrder = 0,
                LocalOriginalPath = staged,
                LocalThumbPath = thumb,
                ContentHashSha1 = sha1,
                SizeBytes = new FileInfo(staged).Length
            };

            db.ProductImages.Add(img);
            await db.SaveChangesAsync(ct);

            await _outbox.WriteAsync("ProductImage", img.Id, "UPSERT", new
            {
                img.PublicId,
                img.ProductId,
                img.IsPrimary,
                img.SortOrder,
                img.ContentHashSha1
            }, ct);

            if (oldPrimaryIds.Count > 0)
            {
                var demoted = await db.ProductImages
                    .Where(pi => oldPrimaryIds.Contains(pi.Id))
                    .ToListAsync(ct);

                foreach (var d in demoted)
                {
                    await _outbox.WriteAsync("ProductImage", d.Id, "UPSERT", new
                    {
                        d.PublicId,
                        d.ProductId,
                        d.IsPrimary,
                        d.SortOrder,
                        d.ContentHashSha1
                    }, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            return img;
        }

        public async Task ClearProductGalleryImagesAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var imgs = await db.ProductImages.Where(pi => pi.ProductId == productId).ToListAsync(ct);
            db.ProductImages.RemoveRange(imgs);
            await db.SaveChangesAsync(ct);
            // if you publish deletes to outbox, emit here for each imageId
        }

        public async Task<ItemImage> SetItemPrimaryImageAsync(
            int itemId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default)
        {
            EnsureMedia();
            await using var db = await _dbf.CreateDbContextAsync(ct);

            _ = await db.Items.FindAsync(new object?[] { itemId }, ct)
                ?? throw new InvalidOperationException("Item not found.");

            var ext = Path.GetExtension(originalLocalPath);
            var stem = $"i{itemId}_{Guid.NewGuid():N}";
            var staged = Path.Combine(MediaPaths.OriginalsDir, stem + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(originalLocalPath, staged, overwrite: true);

            var sha1 = Sha1(staged);
            var thumb = createThumbAt(stem);

            var oldPrimaryIds = await db.ItemImages
                .Where(x => x.ItemId == itemId && x.IsPrimary)
                .Select(x => x.Id)
                .ToListAsync(ct);

            await db.ItemImages
                .Where(x => x.ItemId == itemId && x.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, false), ct);

            var img = new ItemImage
            {
                ItemId = itemId,
                IsPrimary = true,
                SortOrder = 0,
                LocalOriginalPath = staged,
                LocalThumbPath = thumb,
                ContentHashSha1 = sha1,
                SizeBytes = new FileInfo(staged).Length
            };

            db.ItemImages.Add(img);
            await db.SaveChangesAsync(ct);

            await _outbox.WriteAsync("ItemImage", img.Id, "UPSERT", new
            {
                img.PublicId,
                img.ItemId,
                img.IsPrimary,
                img.SortOrder,
                img.ContentHashSha1
            }, ct);

            if (oldPrimaryIds.Count > 0)
            {
                var demoted = await db.ItemImages
                    .Where(ii => oldPrimaryIds.Contains(ii.Id))
                    .ToListAsync(ct);

                foreach (var d in demoted)
                {
                    await _outbox.WriteAsync("ItemImage", d.Id, "UPSERT", new
                    {
                        d.PublicId,
                        d.ItemId,
                        d.IsPrimary,
                        d.SortOrder,
                        d.ContentHashSha1
                    }, ct);
                }
            }

            await db.SaveChangesAsync(ct);
            return img;
        }

        public async Task<ProductImage> AddProductGalleryImageAsync(
            int productId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default)
        {
            EnsureMedia();
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ext = Path.GetExtension(originalLocalPath);
            var stem = $"p{productId}_{Guid.NewGuid():N}";
            var staged = Path.Combine(MediaPaths.OriginalsDir, stem + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(originalLocalPath, staged, overwrite: true);

            var sha1 = Sha1(staged);
            var thumb = createThumbAt(stem);

            var nextOrder = (await db.ProductImages
                .Where(x => x.ProductId == productId)
                .MaxAsync(x => (int?)x.SortOrder, ct)) ?? -1;

            var img = new ProductImage
            {
                ProductId = productId,
                IsPrimary = false,
                SortOrder = nextOrder + 1,
                LocalOriginalPath = staged,
                LocalThumbPath = thumb,
                ContentHashSha1 = sha1,
                SizeBytes = new FileInfo(staged).Length
            };

            db.ProductImages.Add(img);
            await db.SaveChangesAsync(ct);

            await _outbox.WriteAsync("ProductImage", img.Id, "UPSERT", new
            {
                img.PublicId,
                img.ProductId,
                img.IsPrimary,
                img.SortOrder,
                img.ContentHashSha1
            }, ct);

            await db.SaveChangesAsync(ct);
            return img;
        }

        public async Task<ItemImage> AddItemGalleryImageAsync(
            int itemId, string originalLocalPath, Func<string, string> createThumbAt, CancellationToken ct = default)
        {
            EnsureMedia();
            await using var db = await _dbf.CreateDbContextAsync(ct);

            _ = await db.Items.FindAsync(new object?[] { itemId }, ct)
                ?? throw new InvalidOperationException("Item not found.");

            var ext = Path.GetExtension(originalLocalPath);
            var stem = $"i{itemId}_{Guid.NewGuid():N}";
            var staged = Path.Combine(MediaPaths.OriginalsDir, stem + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(originalLocalPath, staged, overwrite: true);

            var sha1 = Sha1(staged);
            var thumb = createThumbAt(stem);

            var nextOrder = (await db.ItemImages
                .Where(x => x.ItemId == itemId)
                .MaxAsync(x => (int?)x.SortOrder, ct)) ?? -1;

            var img = new ItemImage
            {
                ItemId = itemId,
                IsPrimary = false,
                SortOrder = nextOrder + 1,
                LocalOriginalPath = staged,
                LocalThumbPath = thumb,
                ContentHashSha1 = sha1,
                SizeBytes = new FileInfo(staged).Length
            };

            db.ItemImages.Add(img);
            await db.SaveChangesAsync(ct);

            await _outbox.WriteAsync("ItemImage", img.Id, "UPSERT", new
            {
                img.PublicId,
                img.ItemId,
                img.IsPrimary,
                img.SortOrder,
                img.ContentHashSha1
            }, ct);

            await db.SaveChangesAsync(ct);
            return img;
        }

        public async Task DeleteImageAsync(string kind, int imageId, bool deleteFiles = false, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            if (kind == "product")
            {
                var img = await db.ProductImages.FindAsync(new object?[] { imageId }, ct);
                if (img is null) return;
                if (deleteFiles)
                {
                    TryDelete(img.LocalOriginalPath);
                    TryDelete(img.LocalThumbPath);
                }
                db.ProductImages.Remove(img);
                await db.SaveChangesAsync(ct);
                await _outbox.WriteAsync("ProductImage", imageId, "DELETE", new { }, ct);
                await db.SaveChangesAsync(ct);
            }
            else if (kind == "item")
            {
                var img = await db.ItemImages.FindAsync(new object?[] { imageId }, ct);
                if (img is null) return;
                if (deleteFiles)
                {
                    TryDelete(img.LocalOriginalPath);
                    TryDelete(img.LocalThumbPath);
                }
                db.ItemImages.Remove(img);
                await db.SaveChangesAsync(ct);
                await _outbox.WriteAsync("ItemImage", imageId, "DELETE", new { }, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        // ---------- Products ----------
        public async Task<List<Product>> SearchProductsAsync(string? term, int take = 100, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            term = (term ?? "").Trim();
            var q = db.Products
                .AsNoTracking()
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Where(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                q = q.Where(p =>
                    EF.Functions.Like(p.Name, like) ||
                    (p.Brand != null && EF.Functions.Like(p.Brand.Name, like)) ||
                    (p.Category != null && EF.Functions.Like(p.Category.Name, like))
                );
            }

            return await q.OrderBy(p => p.Name)
                          .ThenBy(p => p.Brand != null ? p.Brand.Name : "")
                          .ThenBy(p => p.Category != null ? p.Category.Name : "")
                          .Take(take)
                          .ToListAsync(ct);
        }

        public async Task<Product> CreateProductAsync(string name, int? brandId = null, int? categoryId = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var p = new Product
            {
                Name = name,
                BrandId = brandId,
                CategoryId = categoryId,
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            };
            db.Products.Add(p);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, p, ct);
            await db.SaveChangesAsync(ct);

            return p;
        }

        public async Task<Product?> GetProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        }

        public async Task<Product> UpdateProductAsync(int productId, string name, int? brandId, int? categoryId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var p = await db.Products.FirstAsync(x => x.Id == productId, ct);

            p.Name = (name ?? "").Trim();
            p.BrandId = brandId;
            p.CategoryId = categoryId;
            p.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, p, ct);
            await db.SaveChangesAsync(ct);

            await db.Entry(p).Reference(x => x.Brand).LoadAsync(ct);
            await db.Entry(p).Reference(x => x.Category).LoadAsync(ct);

            return p;
        }

        public async Task<(bool canDelete, string? reason)> CanHardDeleteProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var itemIds = await db.Items
                .Where(i => i.ProductId == productId)
                .Select(i => i.Id)
                .ToListAsync(ct);

            bool hasSales = await db.SaleLines.AnyAsync(sl => itemIds.Contains(sl.ItemId), ct);
            if (hasSales) return (false, "Product has sales.");

            bool hasPurchases = await db.PurchaseLines.AnyAsync(pl => itemIds.Contains(pl.ItemId), ct);
            if (hasPurchases) return (false, "Product has purchases.");

            bool hasStockEntries = await db.StockEntries.AnyAsync(se => itemIds.Contains(se.ItemId), ct);
            if (hasStockEntries) return (false, "Product has stock history (e.g., opening stock).");

            return (true, null);
        }

        public async Task DeleteProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var items = await db.Items.Where(i => i.ProductId == productId).ToListAsync(ct);
            db.Items.RemoveRange(items);

            var product = await db.Products.FirstAsync(p => p.Id == productId, ct);
            db.Products.Remove(product);

            await db.SaveChangesAsync(ct);

            // If you keep a tombstone outbox, enqueue here.

            await tx.CommitAsync(ct);
        }

        public async Task VoidProductAsync(int productId, string user, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var now = DateTime.UtcNow;
            var product = await db.Products.Include(p => p.Variants)
                             .FirstAsync(p => p.Id == productId, ct);

            product.IsActive = false;
            product.IsVoided = true;
            product.VoidedAtUtc = now;
            product.VoidedBy = user;

            foreach (var it in product.Variants)
            {
                it.IsActive = false;
                it.IsVoided = true;
                it.VoidedAtUtc = now;
                it.VoidedBy = user;
            }

            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, product, ct);
            foreach (var it in product.Variants)
                await _outbox.EnqueueUpsertAsync(db, it, ct);
            await db.SaveChangesAsync(ct);
        }

        // ---------- Items / Variants (reads for UI) ----------
        public async Task<List<ItemVariantRow>> GetItemsForProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Items
                .Where(i => i.ProductId == productId)
                .Include(i => i.Brand)
                .Include(i => i.Category)
                .Include(i => i.Product)!.ThenInclude(p => p.Brand)
                .Include(i => i.Product)!.ThenInclude(p => p.Category)
                .OrderBy(i => i.Sku)
                .Select(i => new ItemVariantRow
                {
                    Id = i.Id,
                    Sku = i.Sku,
                    Name = i.Name,
                    ProductName = i.Product!.Name,
                    Barcode = i.Barcodes
                                .OrderByDescending(b => b.IsPrimary)
                                .Select(b => b.Code)
                                .FirstOrDefault(),
                    Price = i.Price,
                    Variant1Name = i.Variant1Name,
                    Variant1Value = i.Variant1Value,
                    Variant2Name = i.Variant2Name,
                    Variant2Value = i.Variant2Value,
                    BrandId = i.BrandId ?? i.Product!.BrandId,
                    BrandName = (i.Brand != null ? i.Brand.Name : i.Product!.Brand != null ? i.Product.Brand.Name : null),
                    CategoryId = i.CategoryId ?? i.Product!.CategoryId,
                    CategoryName = (i.Category != null ? i.Category.Name : i.Product!.Category != null ? i.Product.Category.Name : null),
                    TaxCode = i.TaxCode,
                    DefaultTaxRatePct = i.DefaultTaxRatePct,
                    TaxInclusive = i.TaxInclusive,
                    DefaultDiscountPct = i.DefaultDiscountPct,
                    DefaultDiscountAmt = i.DefaultDiscountAmt,
                    UpdatedAt = i.UpdatedAt,
                    IsActive = i.IsActive,
                    IsVoided = i.IsVoided
                })
                .ToListAsync(ct);
        }

        public async Task<List<ItemVariantRow>> SearchStandaloneItemRowsAsync(string term, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.Items.Where(i => i.ProductId == null);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term.Trim()}%";
                q = q.Where(i =>
                    EF.Functions.Like(i.Name, like) ||
                    EF.Functions.Like(i.Sku, like) ||
                    i.Barcodes.Any(b => EF.Functions.Like(b.Code, like))
                );
            }

            return await q
                .Include(i => i.Brand)
                .Include(i => i.Category)
                .OrderByDescending(i => i.UpdatedAt)
                .Select(i => new ItemVariantRow
                {
                    Id = i.Id,
                    Sku = i.Sku,
                    Name = i.Name,
                    ProductName = null,
                    Barcode = i.Barcodes
                                .OrderByDescending(b => b.IsPrimary)
                                .Select(b => b.Code)
                                .FirstOrDefault(),
                    Price = i.Price,
                    Variant1Name = i.Variant1Name,
                    Variant1Value = i.Variant1Value,
                    Variant2Name = i.Variant2Name,
                    Variant2Value = i.Variant2Value,
                    BrandId = i.BrandId,
                    BrandName = i.Brand != null ? i.Brand.Name : null,
                    CategoryId = i.CategoryId,
                    CategoryName = i.Category != null ? i.Category.Name : null,
                    TaxCode = i.TaxCode,
                    DefaultTaxRatePct = i.DefaultTaxRatePct,
                    TaxInclusive = i.TaxInclusive,
                    DefaultDiscountPct = i.DefaultDiscountPct,
                    DefaultDiscountAmt = i.DefaultDiscountAmt,
                    UpdatedAt = i.UpdatedAt,
                    IsActive = i.IsActive,
                    IsVoided = i.IsVoided
                })
                .ToListAsync(ct);
        }

        public async Task<List<Item>> SearchStandaloneItemsAsync(string? term, int take = 200, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            term = (term ?? "").Trim();
            var q = db.Items.AsNoTracking().Where(i => i.ProductId == null);

            if (!string.IsNullOrEmpty(term))
            {
                var like = $"%{term}%";
                q = q.Where(i =>
                    EF.Functions.Like(i.Name, like) ||
                    EF.Functions.Like(i.Sku, like) ||
                    i.Barcodes.Any(b => EF.Functions.Like(b.Code, like)));
            }

            return await q.OrderBy(i => i.Name).Take(take).ToListAsync(ct);
        }

        // ---------- Items / Variants (writes) ----------
        public async Task<Item> CreateItemAsync(Item i, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            i.UpdatedAt = DateTime.UtcNow;
            db.Items.Add(i);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, i, ct);
            await db.SaveChangesAsync(ct);

            return i;
        }

        public async Task<Item> UpdateItemAsync(Item updated, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var e = await db.Items.FirstAsync(x => x.Id == updated.Id, ct);
            e.Sku = updated.Sku;
            e.Name = updated.Name;
            e.Price = updated.Price;
            e.UpdatedAt = DateTime.UtcNow;

            e.TaxCode = updated.TaxCode;
            e.DefaultTaxRatePct = updated.DefaultTaxRatePct;
            e.TaxInclusive = updated.TaxInclusive;
            e.DefaultDiscountPct = updated.DefaultDiscountPct;
            e.DefaultDiscountAmt = updated.DefaultDiscountAmt;

            e.Variant1Name = updated.Variant1Name;
            e.Variant1Value = updated.Variant1Value;
            e.Variant2Name = updated.Variant2Name;
            e.Variant2Value = updated.Variant2Value;

            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, e, ct);
            await db.SaveChangesAsync(ct);

            return e;
        }

        public async Task<List<Item>> BulkCreateVariantsAsync(
            int productId,
            string itemBaseName,
            string axis1Name, IEnumerable<string> axis1Values,
            string axis2Name, IEnumerable<string> axis2Values,
            decimal price, string? taxCode, decimal taxPct, bool taxInclusive,
            decimal? defDiscPct, decimal? defDiscAmt,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var now = DateTime.UtcNow;
            var list = new List<Item>();

            var a1Name = string.IsNullOrWhiteSpace(axis1Name) ? null : axis1Name.Trim();
            var a2Name = string.IsNullOrWhiteSpace(axis2Name) ? null : axis2Name.Trim();

            foreach (var v1 in axis1Values.Select(s => s.Trim()).Where(s => s.Length > 0))
                foreach (var v2 in axis2Values.Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    var sku = MakeSku(itemBaseName, v1, v2);
                    var name = itemBaseName;

                    var it = new Item
                    {
                        ProductId = productId,
                        Sku = sku,
                        Name = name,
                        Price = price,
                        UpdatedAt = now,

                        TaxCode = taxCode,
                        DefaultTaxRatePct = taxPct,
                        TaxInclusive = taxInclusive,
                        DefaultDiscountPct = defDiscPct,
                        DefaultDiscountAmt = defDiscAmt,

                        Variant1Name = a1Name,
                        Variant1Value = v1,
                        Variant2Name = a2Name,
                        Variant2Value = v2
                    };
                    db.Items.Add(it);
                    list.Add(it);
                }

            await db.SaveChangesAsync(ct);
            foreach (var it in list)
                await _outbox.EnqueueUpsertAsync(db, it, ct);
            await db.SaveChangesAsync(ct);

            return list;
        }

        // ---------- Delete / void items ----------
        public async Task<(bool canDelete, string? reason)> CanHardDeleteItemAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            bool hasStock = await db.StockEntries.AnyAsync(se => se.ItemId == itemId, ct);
            if (hasStock) return (false, "Item has stock history.");

            bool hasSales = await db.SaleLines.AnyAsync(sl => sl.ItemId == itemId, ct);
            if (hasSales) return (false, "Item has sales.");

            bool hasPurchases = await db.PurchaseLines.AnyAsync(pl => pl.ItemId == itemId, ct);
            if (hasPurchases) return (false, "Item has purchases.");

            return (true, null);
        }

        public async Task DeleteItemAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var (can, reason) = await CanHardDeleteItemAsync(itemId, ct);
            if (!can) throw new InvalidOperationException("Cannot delete item: " + reason);

            var it = await db.Items.FirstAsync(x => x.Id == itemId, ct);
            db.Items.Remove(it);
            await db.SaveChangesAsync(ct);

            // If you keep tombstones, enqueue here.
        }

        public async Task VoidItemAsync(int itemId, string user, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var it = await db.Items.FirstAsync(x => x.Id == itemId, ct);
            if (it.IsVoided) return;

            it.IsVoided = true;
            it.IsActive = false;
            it.VoidedAtUtc = DateTime.UtcNow;
            it.VoidedBy = user;
            it.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, it, ct);
            await db.SaveChangesAsync(ct);
        }

        // ---------- Barcodes ----------
        public async Task<ItemBarcode> AddBarcodeAsync(
            int itemId, string code, BarcodeSymbology sym, int qtyPerScan = 1, bool isPrimary = false, string? label = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            code = (code ?? "").Trim();
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("Barcode cannot be empty.");

            bool exists = await db.ItemBarcodes.AnyAsync(x => x.Code == code, ct);
            if (exists) throw new InvalidOperationException("Barcode already exists.");

            if (isPrimary)
            {
                await db.ItemBarcodes.Where(bc => bc.ItemId == itemId && bc.IsPrimary)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPrimary, false), ct);
            }

            var entity = new ItemBarcode
            {
                ItemId = itemId,
                Code = code,
                Symbology = sym,
                QuantityPerScan = Math.Max(1, qtyPerScan),
                IsPrimary = isPrimary,
                Label = string.IsNullOrWhiteSpace(label) ? null : label,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.ItemBarcodes.Add(entity);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, entity, ct);

            // Optional: enqueue all now-nonprimary barcodes (if you want remote to mirror)
            var demoted = await db.ItemBarcodes.Where(b => b.ItemId == itemId && !b.IsPrimary).ToListAsync(ct);
            foreach (var b in demoted)
                await _outbox.EnqueueUpsertAsync(db, b, ct);

            await db.SaveChangesAsync(ct);
            return entity;
        }

        public async Task<(Item item, int qty)> ResolveScanAsync(string scannedCode, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            scannedCode = (scannedCode ?? "").Trim();
            if (string.IsNullOrEmpty(scannedCode)) throw new ArgumentException("Code is empty.");

            var hit = await db.ItemBarcodes
                .Where(x => x.Code == scannedCode)
                .Select(x => new { x.Item, x.QuantityPerScan })
                .FirstOrDefaultAsync(ct);

            if (hit != null) return (hit.Item, Math.Max(1, hit.QuantityPerScan));
            throw new KeyNotFoundException("Barcode not found.");
        }

        public async Task<(bool Taken, string? ProductName, string? ItemName, int? ProductId, int ItemId)>
            TryGetBarcodeOwnerAsync(string code, int? excludeItemId = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            if (string.IsNullOrWhiteSpace(code)) return (false, null, null, null, 0);
            code = code.Trim();

            var q = db.ItemBarcodes
                .Include(b => b.Item).ThenInclude(i => i.Product)
                .Where(b => b.Code == code);

            if (excludeItemId is int eid) q = q.Where(b => b.ItemId != eid);

            var o = await q.Select(b => new
            {
                b.ItemId,
                ProductId = (int?)b.Item.ProductId,
                ProductName = b.Item.Product != null ? b.Item.Product.Name : null,
                ItemName = b.Item.Name
            }).FirstOrDefaultAsync(ct);

            return o is null
                ? (false, null, null, null, 0)
                : (true, o.ProductName, o.ItemName, o.ProductId, o.ItemId);
        }

        public async Task<List<BarcodeConflict>> FindBarcodeConflictsAsync(
            IEnumerable<string> codes, int? excludeItemId = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var list = codes.Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
            if (list.Count == 0) return new();

            var q = db.ItemBarcodes
                .Include(b => b.Item).ThenInclude(i => i.Product)
                .Where(b => list.Contains(b.Code));
            if (excludeItemId is int eid) q = q.Where(b => b.ItemId != eid);

            return await q.Select(b => new BarcodeConflict
            {
                Code = b.Code,
                ItemId = b.ItemId,
                ProductId = b.Item.ProductId,
                ProductName = b.Item.Product != null ? b.Item.Product.Name : null,
                ItemName = b.Item.Name
            }).ToListAsync(ct);
        }

        public async Task<(string Code, int AdvancedBy)> GenerateUniqueBarcodeAsync(
            BarcodeSymbology sym, string prefix, int startSeq, int maxTries = 10000, CancellationToken ct = default)
        {
            for (int i = 0; i < maxTries; i++)
            {
                var candidate = BarcodeUtil.GenerateBySymbology(sym, prefix, startSeq + i);
                var owner = await TryGetBarcodeOwnerAsync(candidate, null, ct);
                if (!owner.Taken)
                    return (candidate, i + 1);
            }
            throw new InvalidOperationException("Could not generate a unique barcode. Adjust Prefix/Start.");
        }

        // ---------- Row <-> Entity mapping ----------
        private static ItemVariantRow ToRow(Item i)
        {
            var primary = i.Barcodes?
                .OrderByDescending(b => b.IsPrimary)
                .Select(b => b.Code)
                .FirstOrDefault();

            return new ItemVariantRow
            {
                Id = i.Id,
                Sku = i.Sku,
                Name = i.Name,
                ProductName = i.Product?.Name,
                Barcode = primary,
                Price = i.Price,
                Variant1Name = i.Variant1Name,
                Variant1Value = i.Variant1Value,
                Variant2Name = i.Variant2Name,
                Variant2Value = i.Variant2Value,
                BrandId = i.BrandId ?? i.Product?.BrandId,
                BrandName = i.Brand?.Name ?? i.Product?.Brand?.Name,
                CategoryId = i.CategoryId ?? i.Product?.CategoryId,
                CategoryName = i.Category?.Name ?? i.Product?.Category?.Name,
                TaxCode = i.TaxCode,
                DefaultTaxRatePct = i.DefaultTaxRatePct,
                TaxInclusive = i.TaxInclusive,
                DefaultDiscountPct = i.DefaultDiscountPct,
                DefaultDiscountAmt = i.DefaultDiscountAmt,
                UpdatedAt = i.UpdatedAt,
                IsActive = i.IsActive,
                IsVoided = i.IsVoided
            };
        }

        // ---------- Queries used by UI (read helpers) ----------
        public async Task<Item?> GetItemWithBarcodesAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Items
                .Include(i => i.Barcodes)
                .Include(i => i.Product).ThenInclude(p => p.Brand)
                .Include(i => i.Product).ThenInclude(p => p.Category)
                .Include(i => i.Brand)
                .Include(i => i.Category)
                .FirstOrDefaultAsync(i => i.Id == itemId, ct);
        }

        public async Task<List<string>> GetProductThumbsAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.ProductImages.AsNoTracking()
                .Where(pi => pi.ProductId == productId)
                .OrderBy(pi => pi.SortOrder)
                .Select(pi => pi.LocalThumbPath!)
                .Where(p => p != null)
                .ToListAsync(ct);
        }

        public async Task<List<string>> GetItemThumbsAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.ItemImages.AsNoTracking()
                .Where(ii => ii.ItemId == itemId)
                .OrderBy(ii => ii.SortOrder)
                .Select(ii => ii.LocalThumbPath!)
                .Where(p => p != null)
                .ToListAsync(ct);
        }

        public async Task<int?> GetProductIdForItemAsync(int itemId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Items.AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => i.ProductId)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<Item> UpdateItemFromRowAsync(ItemVariantRow r, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var e = await db.Items.FirstAsync(i => i.Id == r.Id, ct);

            e.Sku = r.Sku ?? e.Sku;
            if (!string.IsNullOrWhiteSpace(r.Name)) e.Name = r.Name!;
            e.Price = r.Price;
            e.Variant1Name = r.Variant1Name;
            e.Variant1Value = r.Variant1Value;
            e.Variant2Name = r.Variant2Name;
            e.Variant2Value = r.Variant2Value;
            e.BrandId = r.BrandId;
            e.CategoryId = r.CategoryId;
            e.TaxCode = r.TaxCode;
            e.DefaultTaxRatePct = r.DefaultTaxRatePct;
            e.TaxInclusive = r.TaxInclusive;
            e.DefaultDiscountPct = r.DefaultDiscountPct;
            e.DefaultDiscountAmt = r.DefaultDiscountAmt;
            e.IsActive = r.IsActive;
            e.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, e, ct);
            await db.SaveChangesAsync(ct);

            return e;
        }

        public async Task<List<ItemVariantRow>> SaveVariantRowsAsync(IEnumerable<ItemVariantRow> rows, CancellationToken ct = default)
        {
            var list = rows.ToList();
            if (list.Count == 0) return new();

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var r in list)
            {
                var e = await db.Items.FirstAsync(i => i.Id == r.Id, ct);

                e.Sku = r.Sku ?? e.Sku;
                if (!string.IsNullOrWhiteSpace(r.Name)) e.Name = r.Name!;
                e.Price = r.Price;
                e.Variant1Name = r.Variant1Name;
                e.Variant1Value = r.Variant1Value;
                e.Variant2Name = r.Variant2Name;
                e.Variant2Value = r.Variant2Value;
                e.BrandId = r.BrandId;
                e.CategoryId = r.CategoryId;
                e.TaxCode = r.TaxCode;
                e.DefaultTaxRatePct = r.DefaultTaxRatePct;
                e.TaxInclusive = r.TaxInclusive;
                e.DefaultDiscountPct = r.DefaultDiscountPct;
                e.DefaultDiscountAmt = r.DefaultDiscountAmt;
                e.IsActive = r.IsActive;
                e.UpdatedAt = DateTime.UtcNow;

                await _outbox.EnqueueUpsertAsync(db, e, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var ids = list.Select(x => x.Id).Distinct().ToList();
            var fresh = await db.Items
                .Include(i => i.Barcodes)
                .Include(i => i.Product).ThenInclude(p => p.Brand)
                .Include(i => i.Product).ThenInclude(p => p.Category)
                .Include(i => i.Brand)
                .Include(i => i.Category)
                .Where(i => ids.Contains(i.Id))
                .ToListAsync(ct);

            return fresh.Select(ToRow).ToList();
        }

        public async Task<(Item item, string? primaryCode)> ReplaceBarcodesAsync(int itemId, IEnumerable<ItemBarcode> newBarcodes, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var dbItem = await db.Items
                .Include(i => i.Barcodes)
                .FirstAsync(i => i.Id == itemId, ct);

            var codes = newBarcodes
                .Select(b => b.Code?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count != newBarcodes.Count())
                throw new InvalidOperationException("Duplicate barcodes detected in the edited list.");

            var conflict = await db.ItemBarcodes
                .AnyAsync(b => codes.Contains(b.Code) && b.ItemId != dbItem.Id, ct);

            if (conflict)
                throw new InvalidOperationException("One or more barcodes are already used by another item.");

            db.ItemBarcodes.RemoveRange(dbItem.Barcodes);
            dbItem.Barcodes.Clear();

            foreach (var b in newBarcodes)
            {
                var code = b.Code?.Trim();
                if (string.IsNullOrEmpty(code)) continue;

                dbItem.Barcodes.Add(new ItemBarcode
                {
                    ItemId = dbItem.Id,
                    Code = code,
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

            dbItem.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, dbItem, ct);
            await db.SaveChangesAsync(ct);

            var primary = dbItem.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                          ?? dbItem.Barcodes?.FirstOrDefault()?.Code;

            return (dbItem, primary);
        }

        public async Task<ItemVariantRow> EditSingleItemAsync(Item editedWithBarcodes, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var dbItem = await db.Items
                .Include(i => i.Barcodes)
                .Include(i => i.Product).ThenInclude(p => p.Brand)
                .Include(i => i.Product).ThenInclude(p => p.Category)
                .Include(i => i.Brand)
                .Include(i => i.Category)
                .FirstAsync(i => i.Id == editedWithBarcodes.Id, ct);

            dbItem.Sku = editedWithBarcodes.Sku ?? dbItem.Sku;
            if (!string.IsNullOrWhiteSpace(editedWithBarcodes.Name)) dbItem.Name = editedWithBarcodes.Name!;
            dbItem.Price = editedWithBarcodes.Price;
            dbItem.TaxCode = editedWithBarcodes.TaxCode;
            dbItem.DefaultTaxRatePct = editedWithBarcodes.DefaultTaxRatePct;
            dbItem.TaxInclusive = editedWithBarcodes.TaxInclusive;
            dbItem.DefaultDiscountPct = editedWithBarcodes.DefaultDiscountPct;
            dbItem.DefaultDiscountAmt = editedWithBarcodes.DefaultDiscountAmt;
            dbItem.Variant1Name = editedWithBarcodes.Variant1Name;
            dbItem.Variant1Value = editedWithBarcodes.Variant1Value;
            dbItem.Variant2Name = editedWithBarcodes.Variant2Name;
            dbItem.Variant2Value = editedWithBarcodes.Variant2Value;
            dbItem.IsActive = editedWithBarcodes.IsActive;
            dbItem.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            await ReplaceBarcodesAsync(dbItem.Id, editedWithBarcodes.Barcodes ?? Enumerable.Empty<ItemBarcode>(), ct);

            await tx.CommitAsync(ct);

            var fresh = await GetItemWithBarcodesAsync(dbItem.Id, ct);
            return ToRow(fresh!);
        }

        // ---------- Images (read helpers for UI strip) ----------
        public async Task<List<string>> GetAllThumbsForProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var thumbs = new List<string>();

            thumbs.AddRange(await db.ProductImages.AsNoTracking()
                .Where(pi => pi.ProductId == productId)
                .OrderBy(pi => pi.SortOrder)
                .Select(pi => pi.LocalThumbPath!)
                .Where(p => p != null)
                .ToListAsync(ct));

            var vThumbs = await (
                from ii in db.ItemImages.AsNoTracking()
                join it in db.Items.AsNoTracking() on ii.ItemId equals it.Id
                where it.ProductId == productId
                orderby ii.SortOrder
                select ii.LocalThumbPath!
            ).Where(p => p != null).ToListAsync(ct);

            thumbs.AddRange(vThumbs);
            return thumbs;
        }

        // ---------- misc ----------
        private static string MakeSku(string baseName, string v1, string v2)
        {
            string norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var b = norm(baseName.Replace(" ", ""));
            var a = norm(v1);
            var c = norm(v2);
            return $"{b}-{a}-{c}";
        }

        public async Task<List<Brand>> GetActiveBrandsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        }

        public async Task<List<Category>> GetActiveCategoriesAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Categories.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        }

        // ===== VM-friendly queries =====
        public async Task<List<Product>> GetProductsForVmAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Products
                .AsNoTracking()
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.Variants)
                    .ThenInclude(v => v.Barcodes)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);
        }

        public async Task<List<Category>> GetAllCategoriesAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        }

        /// <summary>
        /// Computes the next available numeric sequence for SKUs that look like "PREFIX-###".
        /// </summary>
        public async Task<int> GetNextSkuSequenceAsync(string prefix, int fallbackStart, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var likePrefix = (prefix ?? "") + "-";
            var existing = await db.Items
                .AsNoTracking()
                .Where(i => i.Sku != null && i.Sku.StartsWith(likePrefix))
                .Select(i => i.Sku!)
                .ToListAsync(ct);

            var maxNum = 0;
            foreach (var sku in existing)
            {
                var tail = sku.Substring(likePrefix.Length);
                if (int.TryParse(tail, out var n) && n > maxNum) maxNum = n;
            }
            return Math.Max(maxNum + 1, fallbackStart);
        }

        private static void EnsureMedia()
        {
            Pos.Persistence.Media.MediaPaths.Ensure();
        }

    }
}
