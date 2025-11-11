// Pos.Persistence/Services/ItemsService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.DTO;
using Pos.Domain.Entities;
using Pos.Persistence.Sync; // ⬅️ add
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public class ItemsService : IItemsReadService
    {
        private readonly PosClientDbContext _db;
        private readonly IOutboxWriter _outbox; // ⬅️ add

        public ItemsService(PosClientDbContext db, IOutboxWriter outbox) // ⬅️ change
        {
            _db = db; _outbox = outbox; // ⬅️ add
        }

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
            // === SYNC: item created ===
            await _outbox.EnqueueUpsertAsync(_db, item, default);
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

        public async Task<Dictionary<int, (string display, string sku)>> GetDisplayMetaAsync(
    IEnumerable<int> itemIds,
    CancellationToken ct = default)
        {
            var ids = itemIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return new();

            var rows = await (
                from i in _db.Items.AsNoTracking()
                join pr in _db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                where ids.Contains(i.Id)
                select new
                {
                    i.Id,
                    i.Sku,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value
                }
            ).ToListAsync(ct);

            var dict = new Dictionary<int, (string display, string sku)>(rows.Count);
            foreach (var r in rows)
            {
                var display = Pos.Domain.Formatting.ProductNameComposer.Compose(
                    r.ProductName, r.ItemName,
                    r.Variant1Name, r.Variant1Value,
                    r.Variant2Name, r.Variant2Value
                );
                dict[r.Id] = (display ?? $"Item #{r.Id}", r.Sku ?? "");
            }
            return dict;
        }

        /// <summary>
        /// For a single item: display name, SKU, and last purchase UnitCost (ignoring returns).
        /// </summary>
        public async Task<(string display, string sku, decimal? lastCost)?> GetItemMetaForReturnAsync(
            int itemId, CancellationToken ct = default)
        {
            var meta = await (
                from i in _db.Items.AsNoTracking().Where(x => x.Id == itemId)
                join pr in _db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                select new
                {
                    i.Id,
                    i.Sku,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value
                }
            ).FirstOrDefaultAsync(ct);

            if (meta == null) return null;

            var display = Pos.Domain.Formatting.ProductNameComposer.Compose(
                meta.ProductName, meta.ItemName,
                meta.Variant1Name, meta.Variant1Value,
                meta.Variant2Name, meta.Variant2Value) ?? $"Item #{itemId}";

            var lastCost = await (
                from pl in _db.PurchaseLines.AsNoTracking()
                join pu in _db.Purchases.AsNoTracking() on pl.PurchaseId equals pu.Id
                where pl.ItemId == itemId && !pu.IsReturn
                orderby pu.PurchaseDate descending
                select pl.UnitCost
            ).FirstOrDefaultAsync(ct);

            return (display, meta.Sku ?? "", lastCost);
        }


    }
}
