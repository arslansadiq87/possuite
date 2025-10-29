// Pos.Persistence/Services/ItemsService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.DTO;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    public class ItemsService
    {
        private readonly PosClientDbContext _db;
        public ItemsService(PosClientDbContext db) => _db = db;

        /// <summary>
        /// Search items by SKU, Barcode, or Name (contains). Returns up to {take} items.
        /// </summary>
        public Task<List<Item>> SearchAsync(string? term, int take = 30)
        {
            term = (term ?? string.Empty).Trim();

            // NOTE: Include is optional for filtering; EF will translate .Any() to a JOIN.
            // Keep Include if you want Barcodes loaded in the results.
            IQueryable<Item> q = _db.Items
                .AsNoTracking()
                .Include(i => i.Barcodes); // <-- remove this line if you don't need barcodes loaded

            if (!string.IsNullOrEmpty(term))
            {
                var like = $"%{term}%";

                q = q.Where(i =>
                    EF.Functions.Like(i.Sku, like) ||
                    EF.Functions.Like(i.Name, like) ||
                    i.Barcodes.Any(b => EF.Functions.Like(b.Code, like))   // <-- search in ItemBarcodes
                );
            }

            return q.OrderBy(i => i.Name)
                    .Take(take)
                    .ToListAsync();
        }

        public Task<Item?> GetBySkuOrBarcodeAsync(string codeOrSku)
        {
            codeOrSku = (codeOrSku ?? string.Empty).Trim();

            return _db.Items
                .AsNoTracking()
                .Include(i => i.Barcodes)
                .Where(i => i.Sku == codeOrSku || i.Barcodes.Any(b => b.Code == codeOrSku))
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Quick-create a minimal item (SKU + Name required).
        /// </summary>
        public async Task<Item> CreateAsync(Item item)
        {
            _db.Items.Add(item);
            await _db.SaveChangesAsync();
            return item;
        }

        /// <summary>
        /// Build an in-memory index for fast UI filtering (use in ItemSearchBox).
        /// </summary>
        public async Task<List<ItemIndexDto>> BuildIndexAsync()
        {
            // Prefer server-side projection to avoid pulling heavy entity graphs.
            var list = await
            (from i in _db.Items.AsNoTracking()
             join p in _db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
             from p in gp.DefaultIfEmpty()
             let primaryBarcode = _db.ItemBarcodes
                 .Where(b => b.ItemId == i.Id && b.IsPrimary)
                 .Select(b => b.Code).FirstOrDefault()
             let anyBarcode = _db.ItemBarcodes
                 .Where(b => b.ItemId == i.Id)
                 .Select(b => b.Code).FirstOrDefault()
             orderby i.Name
             select new ItemIndexDto(
                 i.Id, i.Name, i.Sku,
                 primaryBarcode ?? anyBarcode,
                 i.Price, i.TaxCode, i.DefaultTaxRatePct, i.TaxInclusive,
                 i.DefaultDiscountPct, i.DefaultDiscountAmt,
                 p != null ? p.Name : null,
                 i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value
             )).ToListAsync();

            return list;
        }

        /// <summary>
        /// Exact barcode/SKU, else name/variant/product starts-with (fast DB lookup).
        /// </summary>
        public Task<ItemIndexDto?> FindOneAsync(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return Task.FromResult<ItemIndexDto?>(null)!;

            // Single query with projection to ItemIndexDto
            return
            (from i in _db.Items.AsNoTracking()
             join p in _db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
             from p in gp.DefaultIfEmpty()
             where
                 _db.ItemBarcodes.Any(b => b.ItemId == i.Id && b.Code == text) ||
                 i.Sku == text ||
                 EF.Functions.Like(EF.Functions.Collate(i.Name, "NOCASE"), text + "%") ||
                 (i.Variant1Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant1Value, "NOCASE"), text + "%")) ||
                 (i.Variant2Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant2Value, "NOCASE"), text + "%")) ||
                 (p != null && EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), text + "%"))
             orderby i.Name
             select new ItemIndexDto(
                 i.Id, i.Name, i.Sku,
                 _db.ItemBarcodes.Where(b => b.ItemId == i.Id && b.IsPrimary).Select(b => b.Code).FirstOrDefault()
                 ?? _db.ItemBarcodes.Where(b => b.ItemId == i.Id).Select(b => b.Code).FirstOrDefault(),
                 i.Price, i.TaxCode, i.DefaultTaxRatePct, i.TaxInclusive,
                 i.DefaultDiscountPct, i.DefaultDiscountAmt,
                 p != null ? p.Name : null,
                 i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value
             )).FirstOrDefaultAsync();
        }
    }
}
