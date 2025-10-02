// Pos.Persistence/Services/ItemsService.cs
using Microsoft.EntityFrameworkCore;
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
    }
}
