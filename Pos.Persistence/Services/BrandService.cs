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
    public sealed class BrandService : IBrandService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public BrandService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<List<BrandRowDto>> SearchAsync(string? term, bool includeInactive, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            term = (term ?? string.Empty).Trim();

            var brands = db.Brands.AsNoTracking();
            if (!includeInactive)
                brands = brands.Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                brands = brands.Where(b => EF.Functions.Like(b.Name, like));
            }

            var products = db.Products.AsNoTracking()
                .Where(p => p.IsActive && !p.IsVoided && p.BrandId != null);

            var items = db.Items.AsNoTracking()
                .Where(i => i.IsActive && !i.IsVoided);

            // products with variants → count variants by product → attribute to BrandId
            var productsWithVariantsCounts =
                items.Where(i => i.ProductId != null)
                     .GroupBy(i => i.ProductId!.Value)
                     .Select(g => new { ProductId = g.Key, VariantCount = g.Count() })
                     .Join(products, pvc => pvc.ProductId, p => p.Id,
                           (pvc, p) => new { BrandId = p.BrandId!.Value, Count = pvc.VariantCount })
                     .GroupBy(x => x.BrandId)
                     .Select(g => new { BrandId = g.Key, Count = g.Sum(x => x.Count) });

            // products without variants → count products, attribute to BrandId
            var productsWithoutVariantsCounts =
                products.Where(p => !items.Any(i => i.ProductId == p.Id))
                        .GroupBy(p => p.BrandId!.Value)
                        .Select(g => new { BrandId = g.Key, Count = g.Count() });

            // standalone items with BrandId (no product)
            var standaloneItemCounts =
                items.Where(i => i.ProductId == null && i.BrandId != null)
                     .GroupBy(i => i.BrandId!.Value)
                     .Select(g => new { BrandId = g.Key, Count = g.Count() });

            var skuCountsByBrand =
                productsWithVariantsCounts
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
                .ToListAsync(ct);

            return rows;
        }

        public async Task SetActiveAsync(int brandId, bool active, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var b = await db.Brands.FirstOrDefaultAsync(x => x.Id == brandId, ct);
            if (b is null) return;

            b.IsActive = active;
            b.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, b, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task<Brand> SaveBrandAsync(int? id, string name, bool isActive, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            var exists = await db.Brands
                .AnyAsync(b => b.Name.ToLower() == name.ToLower() && b.Id != (id ?? 0), ct);

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
                entity = await db.Brands.FirstAsync(b => b.Id == id.Value, ct);
                entity.Name = name;
                entity.IsActive = isActive;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);

            return entity;
        }

        public async Task<Brand?> GetBrandAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        }
    }
}
