// Pos.Persistence/Services/CatalogService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models;


namespace Pos.Persistence.Services
{
    public class CatalogService
    {
        private readonly PosClientDbContext _db;
        public CatalogService(PosClientDbContext db) => _db = db;

        // -------- Products --------
        public Task<List<Product>> SearchProductsAsync(string? term, int take = 100)
        {
            term = (term ?? "").Trim();
            var q = _db.Products.AsNoTracking().Include(p => p.Brand).Where(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                q = q.Where(p =>
                    EF.Functions.Like(p.Name, like) ||
                    (p.Brand != null && EF.Functions.Like(p.Brand.Name, like))
                );
            }

            return q.OrderBy(p => p.Name)
                    .ThenBy(p => p.Brand!.Name)
                    .Take(take)
                    .ToListAsync();
        }


        public async Task<Product> CreateProductAsync(string name, int? brandId = null, int? categoryId = null)
        {
            var p = new Product
            {
                Name = name,
                BrandId = brandId,
                CategoryId = categoryId,
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Products.Add(p);
            await _db.SaveChangesAsync();
            return p;
        }


        public async Task<Product?> GetProductAsync(int productId)
            => await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);

        // -------- Items / Variants --------

        public async Task<List<ItemVariantRow>> GetItemsForProductAsync(int productId)
        {
            return await _db.Items
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
                    Barcode = i.Barcode,
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
                    UpdatedAt = i.UpdatedAt
                })
                .ToListAsync();
        }

        public async Task<List<ItemVariantRow>> SearchStandaloneItemRowsAsync(string term)
        {
            var q = _db.Items
                .Where(i => i.ProductId == null);

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(i => i.Name.Contains(term) || i.Sku.Contains(term) || i.Barcode.Contains(term));
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
                    Barcode = i.Barcode,
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
                    UpdatedAt = i.UpdatedAt
                })
                .ToListAsync();
        }




        public async Task<Item> CreateItemAsync(Item i)
        {
            i.UpdatedAt = DateTime.UtcNow;
            _db.Items.Add(i);
            await _db.SaveChangesAsync();
            return i;
        }

        public async Task<Item> UpdateItemAsync(Item updated)
        {
            var e = await _db.Items.FirstAsync(x => x.Id == updated.Id);
            e.Sku = updated.Sku;
            e.Name = updated.Name;
            e.Barcode = updated.Barcode;
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

            await _db.SaveChangesAsync();
            return e;
        }

        // Bulk add: Cartesian of axis1 x axis2 values → items
        public async Task<List<Item>> BulkCreateVariantsAsync(
            int productId,
            string itemBaseName,
            string axis1Name, IEnumerable<string> axis1Values,
            string axis2Name, IEnumerable<string> axis2Values,
            decimal price, string? taxCode, decimal taxPct, bool taxInclusive,
            decimal? defDiscPct, decimal? defDiscAmt)
        {
            var now = DateTime.UtcNow;
            var list = new List<Item>();

            var a1Name = string.IsNullOrWhiteSpace(axis1Name) ? null : axis1Name.Trim();
            var a2Name = string.IsNullOrWhiteSpace(axis2Name) ? null : axis2Name.Trim();

            foreach (var v1 in axis1Values.Select(s => s.Trim()).Where(s => s.Length > 0))
                foreach (var v2 in axis2Values.Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    var sku = MakeSku(itemBaseName, v1, v2);
                    var name = itemBaseName; // store product name in Product; Item.Name can mirror product or be specific

                    var it = new Item
                    {
                        ProductId = productId,
                        Sku = sku,
                        Name = name,
                        Barcode = "",                    // optional to fill later
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
                    _db.Items.Add(it);
                    list.Add(it);
                }

            await _db.SaveChangesAsync();
            return list;
        }

        public Task<List<Item>> SearchStandaloneItemsAsync(string? term, int take = 200)
        {
            term = (term ?? "").Trim();
            var q = _db.Items.AsNoTracking().Where(i => i.ProductId == null);

            if (!string.IsNullOrEmpty(term))
            {
                var like = $"%{term}%";
                q = q.Where(i =>
                    EF.Functions.Like(i.Name, like) ||
                    EF.Functions.Like(i.Sku, like) ||
                    EF.Functions.Like(i.Barcode, like));
            }

            return q.OrderBy(i => i.Name).Take(take).ToListAsync();
        }


        private static string MakeSku(string baseName, string v1, string v2)
        {
            // Simple SKU maker: BASE-<v1>-<v2> (safe chars)
            string norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var b = norm(baseName.Replace(" ", ""));
            var a = norm(v1);
            var c = norm(v2);
            return $"{b}-{a}-{c}";
        }

        public async Task<(bool canDelete, string? reason)> CanHardDeleteProductAsync(int productId)
        {
            var itemIds = await _db.Items
                .Where(i => i.ProductId == productId)
                .Select(i => i.Id)
                .ToListAsync();

            // If product has no variants, still consider “itemIds” empty;
            // but standalone delete path will handle Items separately.

            // Any sales?
            bool hasSales = await _db.SaleLines.AnyAsync(sl => itemIds.Contains(sl.ItemId));
            if (hasSales) return (false, "Product has sales.");

            // Any purchases?
            bool hasPurchases = await _db.PurchaseLines.AnyAsync(pl => itemIds.Contains(pl.ItemId));
            if (hasPurchases) return (false, "Product has purchases.");

            // Any stock entries at all (includes opening stock, transfers, adjustments)?
            bool hasStockEntries = await _db.StockEntries.AnyAsync(se => itemIds.Contains(se.ItemId));
            if (hasStockEntries) return (false, "Product has stock history (e.g., opening stock).");

            return (true, null);
        }

        public async Task DeleteProductAsync(int productId)
        {
            // Call CanHardDeleteProductAsync before this; throw if not allowed.
            using var tx = await _db.Database.BeginTransactionAsync();

            var items = await _db.Items.Where(i => i.ProductId == productId).ToListAsync();
            _db.Items.RemoveRange(items);

            var product = await _db.Products.FirstAsync(p => p.Id == productId);
            _db.Products.Remove(product);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task VoidProductAsync(int productId, string user)
        {
            var now = DateTime.UtcNow;
            var product = await _db.Products.Include(p => p.Variants)
                             .FirstAsync(p => p.Id == productId);

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

            await _db.SaveChangesAsync();
        }

        // Standalone item versions
        public async Task<(bool canDelete, string? reason)> CanHardDeleteItemAsync(int itemId)
        {
            bool hasSales = await _db.SaleLines.AnyAsync(sl => sl.ItemId == itemId);
            if (hasSales) return (false, "Item has sales.");

            bool hasPurchases = await _db.PurchaseLines.AnyAsync(pl => pl.ItemId == itemId);
            if (hasPurchases) return (false, "Item has purchases.");

            bool hasStock = await _db.StockEntries.AnyAsync(se => se.ItemId == itemId);
            if (hasStock) return (false, "Item has stock history.");
            return (true, null);
        }

        public async Task DeleteItemAsync(int itemId)
        {
            var entity = await _db.Items.FirstAsync(i => i.Id == itemId);
            _db.Items.Remove(entity);
            await _db.SaveChangesAsync();
        }

        public async Task VoidItemAsync(int itemId, string user)
        {
            var it = await _db.Items.FirstAsync(i => i.Id == itemId);
            it.IsActive = false;
            it.IsVoided = true;
            it.VoidedAtUtc = DateTime.UtcNow;
            it.VoidedBy = user;
            await _db.SaveChangesAsync();
        }

    }
}
