using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public class BrandService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public BrandService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public sealed class BrandRowDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public int ItemCount { get; set; }
            public DateTime? CreatedAtUtc { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
        }

        public async Task<List<BrandRowDto>> SearchAsync(string? term, bool includeInactive)
        {
            await using var db = _dbf.CreateDbContext();
            term = (term ?? "").Trim().ToLower();

            var brands = db.Brands.AsNoTracking();
            if (!includeInactive)
                brands = brands.Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
                brands = brands.Where(b => b.Name.ToLower().Contains(term));

            var products = db.Products.AsNoTracking()
                .Where(p => p.IsActive && !p.IsVoided && p.BrandId != null);
            var items = db.Items.AsNoTracking().Where(i => i.IsActive && !i.IsVoided);

            var productsWithVariantsCounts =
                items.Where(i => i.ProductId != null)
                     .GroupBy(i => i.ProductId!.Value)
                     .Select(g => new { ProductId = g.Key, VariantCount = g.Count() })
                     .Join(products, pvc => pvc.ProductId, p => p.Id,
                           (pvc, p) => new { BrandId = p.BrandId!.Value, Count = pvc.VariantCount })
                     .GroupBy(x => x.BrandId)
                     .Select(g => new { BrandId = g.Key, Count = g.Sum(x => x.Count) });

            var productsWithoutVariantsCounts =
                products.Where(p => !items.Any(i => i.ProductId == p.Id))
                        .GroupBy(p => p.BrandId!.Value)
                        .Select(g => new { BrandId = g.Key, Count = g.Count() });

            var standaloneItemCounts =
                items.Where(i => i.ProductId == null && i.BrandId != null)
                     .GroupBy(i => i.BrandId!.Value)
                     .Select(g => new { BrandId = g.Key, Count = g.Count() });

            var skuCountsByBrand = productsWithVariantsCounts
                .Concat(productsWithoutVariantsCounts)
                .Concat(standaloneItemCounts)
                .GroupBy(x => x.BrandId)
                .Select(g => new { BrandId = g.Key, Count = g.Sum(x => x.Count) });

            var rows = await brands
                .GroupJoin(skuCountsByBrand, b => b.Id, sc => sc.BrandId,
                           (b, sc) => new { b, sc = sc.FirstOrDefault() })
                .Select(x => new BrandRowDto
                {
                    Id = x.b.Id,
                    Name = x.b.Name,
                    IsActive = x.b.IsActive,
                    ItemCount = x.sc != null ? x.sc.Count : 0,
                    CreatedAtUtc = x.b.CreatedAtUtc,
                    UpdatedAtUtc = x.b.UpdatedAtUtc
                })
                .OrderBy(r => r.Name)
                .Take(2000)
                .ToListAsync();

            return rows;
        }

        public async Task SetActiveAsync(int brandId, bool active)
        {
            await using var db = _dbf.CreateDbContext();
            var b = await db.Brands.FirstOrDefaultAsync(x => x.Id == brandId);
            if (b == null) return;

            b.IsActive = active;
            b.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // enqueue sync record using core persistence method
            await _outbox.EnqueueUpsertAsync(db, b, default);
            await db.SaveChangesAsync();
        }

        // ==========================
        // Create or Update Brand
        // ==========================
        public async Task<Brand> SaveBrandAsync(int? id, string name, bool isActive)
        {
            await using var db = _dbf.CreateDbContext();

            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            bool exists = await db.Brands
                .AnyAsync(b => b.Name.ToLower() == name.ToLower() && b.Id != (id ?? 0));
            if (exists)
                throw new InvalidOperationException("A brand with this name already exists.");

            Brand entity;
            if (id is null)
            {
                var now = DateTime.UtcNow;
                entity = new Brand
                {
                    Name = name,
                    IsActive = isActive,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.Brands.Add(entity);
            }
            else
            {
                entity = await db.Brands.FirstAsync(b => b.Id == id.Value);
                entity.Name = name;
                entity.IsActive = isActive;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            await _outbox.EnqueueUpsertAsync(db, entity, default);
            await db.SaveChangesAsync();

            return entity;
        }

        public async Task<Brand?> GetBrandAsync(int id)
        {
            await using var db = _dbf.CreateDbContext();
            return await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        }


    }
}
