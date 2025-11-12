using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Reports;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Centralized, read-only reporting queries (no UI, no WPF, no ViewModel math).
    /// </summary>
    public sealed class ReportsService : IReportsService
    {
        private readonly PosClientDbContext _db;
        public ReportsService(PosClientDbContext db) => _db = db;

        private IQueryable<StockEntry> ScopedLedger(InventoryLocationType scopeType, int? scopeId)
        {
            var q = _db.StockEntries.AsNoTracking().Where(s => s.LocationType == scopeType);
            return scopeId is null ? q : q.Where(s => s.LocationId == scopeId.Value);
        }

        public async Task<List<StockOnHandItemRow>> StockOnHandByItemAsync(
            InventoryLocationType scopeType, int? scopeId, CancellationToken ct = default)
        {
            var ledger = ScopedLedger(scopeType, scopeId);

            // Left joins for optional Product/Brand/Category
            var raw = await (from i in _db.Items.AsNoTracking()
                             join p0 in _db.Products.AsNoTracking() on i.ProductId equals p0.Id into gp
                             from p in gp.DefaultIfEmpty()
                             join b0 in _db.Set<Brand>().AsNoTracking() on p.BrandId equals b0.Id into gb
                             from b in gb.DefaultIfEmpty()
                             join c0 in _db.Set<Category>().AsNoTracking() on p.CategoryId equals c0.Id into gc
                             from c in gc.DefaultIfEmpty()
                             join se in ledger on i.Id equals se.ItemId into gse
                             let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m
                             orderby (p != null ? p.Name : i.Name), i.Variant1Value, i.Variant2Value, i.Sku
                             select new
                             {
                                 i.Sku,
                                 ItemName = i.Name,
                                 ProductName = p != null ? p.Name : null,
                                 BrandName = b != null ? b.Name : null,
                                 CategoryName = c != null ? c.Name : null,
                                 i.Variant1Name,
                                 i.Variant1Value,
                                 i.Variant2Name,
                                 i.Variant2Value,
                                 OnHand = onHand
                             })
                             .ToListAsync(ct);

            static string BuildVariant(string? n1, string? v1, string? n2, string? v2)
            {
                var p1 = string.IsNullOrWhiteSpace(v1) ? "" : $"{n1}: {v1}";
                var p2 = string.IsNullOrWhiteSpace(v2) ? "" : $"{(p1.Length > 0 ? "  " : "")}{n2}: {v2}";
                return (p1 + p2).Trim();
            }

            static string BuildDisplay(string? productName, string itemName, string? v1n, string? v1v, string? v2n, string? v2v)
            {
                var baseName = string.IsNullOrWhiteSpace(productName) ? itemName : productName;
                var v = BuildVariant(v1n, v1v, v2n, v2v);
                return string.IsNullOrWhiteSpace(v) ? baseName : $"{baseName} — {v}";
            }

            return raw.Select(x => new StockOnHandItemRow
            {
                Sku = x.Sku,
                DisplayName = BuildDisplay(x.ProductName, x.ItemName, x.Variant1Name, x.Variant1Value, x.Variant2Name, x.Variant2Value),
                Brand = x.BrandName ?? "",
                Category = x.CategoryName ?? "",
                Variant = BuildVariant(x.Variant1Name, x.Variant1Value, x.Variant2Name, x.Variant2Value),
                // match your current UI behavior (round to whole)
                OnHand = (int)global::System.Math.Round(x.OnHand, global::System.MidpointRounding.AwayFromZero)
            }).ToList();
        }

        public async Task<List<StockOnHandProductRow>> StockOnHandByProductAsync(
            InventoryLocationType scopeType, int? scopeId, CancellationToken ct = default)
        {
            var ledger = ScopedLedger(scopeType, scopeId);

            var rows = await (from i in _db.Items.AsNoTracking()
                              join p0 in _db.Products.AsNoTracking() on i.ProductId equals p0.Id into gp
                              from p in gp.DefaultIfEmpty()
                              join b0 in _db.Set<Brand>().AsNoTracking() on p.BrandId equals b0.Id into gb
                              from b in gb.DefaultIfEmpty()
                              join c0 in _db.Set<Category>().AsNoTracking() on p.CategoryId equals c0.Id into gc
                              from c in gc.DefaultIfEmpty()
                              join se in ledger on i.Id equals se.ItemId into gse
                              let onHand = gse.Sum(x => (decimal?)x.QtyChange) ?? 0m
                              group new { onHand, Brand = b != null ? b.Name : "", Category = c != null ? c.Name : "" }
                              by new
                              {
                                  Prod = p != null ? p.Name : i.Name,
                                  BrandName = b != null ? b.Name : "",
                                  CategoryName = c != null ? c.Name : ""
                              }
                              into g
                              orderby g.Key.Prod
                              select new StockOnHandProductRow
                              {
                                  Product = g.Key.Prod,
                                  Brand = g.Key.BrandName,
                                  Category = g.Key.CategoryName,
                                  OnHand = (int)global::System.Math.Round(g.Sum(x => x.onHand), global::System.MidpointRounding.AwayFromZero)
                              })
                              .ToListAsync(ct);

            return rows;
        }

        public async Task<List<Outlet>> GetOutletsForUserAsync(int userId, bool isAdmin, CancellationToken ct = default)
        {
            if (isAdmin)
            {
                return await _db.Outlets.AsNoTracking()
                    .OrderBy(o => o.Name)
                    .ToListAsync(ct);
            }

            // Only outlets assigned to this user
            var assignedIds = await _db.Set<UserOutlet>().AsNoTracking()
                .Where(uo => uo.UserId == userId)
                .Select(uo => uo.OutletId)
                .ToListAsync(ct);

            return await _db.Outlets.AsNoTracking()
                .Where(o => assignedIds.Contains(o.Id))
                .OrderBy(o => o.Name)
                .ToListAsync(ct);
        }

        public Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default)
            => _db.Warehouses.AsNoTracking()
                .OrderBy(w => w.Name)
                .ToListAsync(ct);
    }
}
