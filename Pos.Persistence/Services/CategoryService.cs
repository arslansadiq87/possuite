using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public class CategoryService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public CategoryService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public sealed class CategoryRowDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public int ItemCount { get; set; }
            public DateTime? CreatedAtUtc { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
        }

        public async Task<List<CategoryRowDto>> SearchAsync(string? term, bool includeInactive)
        {
            await using var db = _dbf.CreateDbContext();
            term = (term ?? "").Trim().ToLower();

            var cats = db.Categories.AsNoTracking();
            if (!includeInactive)
                cats = cats.Where(c => c.IsActive);
            if (!string.IsNullOrWhiteSpace(term))
                cats = cats.Where(c => c.Name.ToLower().Contains(term));

            var products = db.Products.AsNoTracking()
                .Where(p => p.IsActive && !p.IsVoided && p.CategoryId != null);
            var items = db.Items.AsNoTracking().Where(i => i.IsActive && !i.IsVoided);

            var prodWithVarCounts =
                items.Where(i => i.ProductId != null)
                     .GroupBy(i => i.ProductId!.Value)
                     .Select(g => new { ProductId = g.Key, VariantCount = g.Count() })
                     .Join(products, pvc => pvc.ProductId, p => p.Id,
                           (pvc, p) => new { CategoryId = p.CategoryId!.Value, Count = pvc.VariantCount })
                     .GroupBy(x => x.CategoryId)
                     .Select(g => new { CategoryId = g.Key, Count = g.Sum(x => x.Count) });

            var prodNoVarCounts =
                products.Where(p => !items.Any(i => i.ProductId == p.Id))
                        .GroupBy(p => p.CategoryId!.Value)
                        .Select(g => new { CategoryId = g.Key, Count = g.Count() });

            var standaloneItemCounts =
                items.Where(i => i.ProductId == null && i.CategoryId != null)
                     .GroupBy(i => i.CategoryId!.Value)
                     .Select(g => new { CategoryId = g.Key, Count = g.Count() });

            var skuCountsByCategory =
                prodWithVarCounts
                    .Concat(prodNoVarCounts)
                    .Concat(standaloneItemCounts)
                    .GroupBy(x => x.CategoryId)
                    .Select(g => new { CategoryId = g.Key, Count = g.Sum(x => x.Count) });

            var rows = await cats
                .GroupJoin(skuCountsByCategory, c => c.Id, sc => sc.CategoryId,
                           (c, sc) => new { c, sc = sc.FirstOrDefault() })
                .Select(x => new CategoryRowDto
                {
                    Id = x.c.Id,
                    Name = x.c.Name,
                    IsActive = x.c.IsActive,
                    ItemCount = x.sc != null ? x.sc.Count : 0,
                    CreatedAtUtc = x.c.CreatedAtUtc,
                    UpdatedAtUtc = x.c.UpdatedAtUtc
                })
                .OrderBy(r => r.Name)
                .Take(2000)
                .ToListAsync();

            return rows;
        }

        public async Task SetActiveAsync(int categoryId, bool active)
        {
            await using var db = _dbf.CreateDbContext();
            var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == categoryId);
            if (c == null) return;

            c.IsActive = active;
            c.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await _outbox.EnqueueUpsertAsync(db, c, default);
            await db.SaveChangesAsync();
        }

        // ==========================
        // Create or Update Category
        // ==========================
        public async Task<Category> SaveCategoryAsync(int? id, string name, bool isActive)
        {
            await using var db = _dbf.CreateDbContext();

            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            bool exists = await db.Categories
                .AnyAsync(c => c.Name.ToLower() == name.ToLower() && c.Id != (id ?? 0));
            if (exists)
                throw new InvalidOperationException("A category with this name already exists.");

            Category entity;
            if (id is null)
            {
                var now = DateTime.UtcNow;
                entity = new Category
                {
                    Name = name,
                    IsActive = isActive,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.Categories.Add(entity);
            }
            else
            {
                entity = await db.Categories.FirstAsync(c => c.Id == id.Value);
                entity.Name = name;
                entity.IsActive = isActive;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            await _outbox.EnqueueUpsertAsync(db, entity, default);
            await db.SaveChangesAsync();

            return entity;
        }

        public async Task<Category?> GetCategoryAsync(int id)
        {
            await using var db = _dbf.CreateDbContext();
            return await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        }

    }
}
