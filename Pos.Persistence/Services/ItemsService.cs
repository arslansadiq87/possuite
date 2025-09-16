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

            IQueryable<Item> q = _db.Items.AsNoTracking();

            if (!string.IsNullOrEmpty(term))
            {
                var like = $"%{term}%";
                q = q.Where(i =>
                    EF.Functions.Like(i.Sku, like) ||
                    EF.Functions.Like(i.Barcode, like) ||
                    EF.Functions.Like(i.Name, like));
            }

            return q.OrderBy(i => i.Name)
                    .Take(take)
                    .ToListAsync();
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
