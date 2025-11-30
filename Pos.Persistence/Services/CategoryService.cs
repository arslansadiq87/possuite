using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models.Catalog;
using Pos.Domain.Services;
using Pos.Persistence.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public sealed class CategoryService : ICategoryService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;
        
        public CategoryService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<List<CategoryRowDto>> SearchAsync(string? term, bool includeInactive, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            term = (term ?? string.Empty).Trim();

            var cats = db.Categories.AsNoTracking();
            if (!includeInactive)
                cats = cats.Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                // Case-insensitive search using LIKE so EF translates on SQLite/PostgreSQL
                var like = $"%{term}%";
                cats = cats.Where(c => EF.Functions.Like(c.Name, like));
            }

            var products = db.Products.AsNoTracking()
                .Where(p => p.IsActive && !p.IsVoided && p.CategoryId != null);

            var items = db.Items.AsNoTracking()
                .Where(i => i.IsActive && !i.IsVoided);

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
                .ToListAsync(ct);

            return rows;
        }

        public async Task SetActiveAsync(int categoryId, bool active, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == categoryId, ct);
            if (c is null) return;

            c.IsActive = active;
            c.UpdatedAtUtc = DateTime.UtcNow;

            // For updates we need the Id in the payload — do a save, enqueue, save.
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, c, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task<Category> SaveCategoryAsync(int? id, string name, bool isActive, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            var nameLower = name.ToLowerInvariant();
            var exists = await db.Categories
                .AnyAsync(c => c.Name.ToLower() == nameLower && c.Id != (id ?? 0), ct);

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
                entity = await db.Categories.FirstAsync(c => c.Id == id.Value, ct);
                entity.Name = name;
                entity.IsActive = isActive;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            // Persist to get Id for outbox payloads (especially for inserts)
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);

            return entity;
        }

        public async Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        public async Task<bool> ProductNameExistsAsync(string name, int? excludeProductId = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var n = (name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(n)) return false;

            return await db.Products
                .AsNoTracking()
                .Where(p => excludeProductId == null || p.Id != excludeProductId.Value)
                .AnyAsync(p => EF.Functions.Like(p.Name, n)            // fast path
                            || p.Name.ToLower().Trim() == n.ToLower(), // hard CI check
                    ct);
        }

        public async Task<Category?> GetOrCreateAsync(string name, bool createIfMissing, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var existing = await db.Categories.FirstOrDefaultAsync(c => c.Name == name, ct);
            if (existing != null) return existing;
            if (!createIfMissing) return null;
            var c = new Category { Name = name, IsActive = true };
            db.Categories.Add(c);
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, c, ct);
            await db.SaveChangesAsync(ct);
            return c;
        }


    }
}
