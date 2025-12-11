using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using Pos.Persistence;                    // PosDbContext
using Pos.Domain.Models.Reports;          // StockValuationRow (DTO)
using Pos.Domain.Services;                // IStockValuationReadService  <-- FIX
using Pos.Domain.Entities;                   // InventoryLocationType       <-- FIX

namespace Pos.Persistence.Services
{
    public class StockValuationReadService : IStockValuationReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public StockValuationReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        /// <summary>
        /// COST VIEW: On-hand at outlet + weighted avg cost (incoming entries) up to cutoffUtc (if provided).
        /// Mirrors the verified SQL.
        /// </summary>
        public async Task<IReadOnlyList<StockValuationRow>> GetCostViewAsync(
            int outletId,
            DateTime? cutoffUtc,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Base filter: Outlet location
            var entries = db.StockEntries.AsNoTracking()
                .Where(e => e.LocationType == InventoryLocationType.Outlet   // <-- FIX (enum compare)
                         && e.LocationId == outletId);

            if (cutoffUtc.HasValue)
                entries = entries.Where(e => e.Ts <= cutoffUtc.Value);

            // On-hand per item
            var onHandDict = await entries
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    OnHand = Math.Round(g.Sum(x => x.QtyChange), 3)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.OnHand, ct);

            // Average cost per item from incoming entries only (QtyChange > 0)
            // We compute sums in SQL, then divide on the client to avoid nullable math issues.
            var avgCostRaw = await entries
                .Where(e => e.QtyChange > 0)
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    SumQty = g.Sum(x => x.QtyChange),
                    SumWeighted = g.Sum(x => x.QtyChange * x.UnitCost)
                })
                .ToListAsync(ct);

            var avgCostDict = avgCostRaw.ToDictionary(
                k => k.ItemId,
                v => v.SumQty == 0m ? 0m : v.SumWeighted / v.SumQty
            );

            // Catalog join
            var items = await db.Items.AsNoTracking()
                .Select(i => new
                {
                    i.Id,
                    i.Sku,
                    DisplayName = i.Name,
                    Brand = i.Brand != null ? i.Brand.Name : "",
                    Category = i.Category != null ? i.Category.Name : "",
                    UnitPrice = i.Price         // decimal (non-nullable)  <-- FIX (no ??)
                })
                .ToListAsync(ct);

            // Build rows (only non-zero on-hand)
            var rows = items
                .Where(i => onHandDict.TryGetValue(i.Id, out var qty) && qty != 0m)
                .Select(i =>
                {
                    var qty = onHandDict[i.Id];
                    avgCostDict.TryGetValue(i.Id, out var uCost);
                    uCost = Math.Round(uCost, 4, MidpointRounding.AwayFromZero);

                    return new StockValuationRow
                    {
                        Sku = i.Sku ?? "",
                        DisplayName = i.DisplayName ?? "",
                        Brand = i.Brand ?? "",
                        Category = i.Category ?? "",
                        OnHand = qty,
                        UnitCost = uCost,
                        UnitPrice = Math.Round(i.UnitPrice, 2, MidpointRounding.AwayFromZero),
                    };
                })
                .OrderBy(r => r.DisplayName)
                .ToList();

            return rows;
        }

        /// <summary>
        /// SALE VIEW: On-hand at outlet + unit price up to cutoffUtc (if provided).
        /// Mirrors the verified SQL.
        /// </summary>
        public async Task<IReadOnlyList<StockValuationRow>> GetSaleViewAsync(
            int outletId,
            DateTime? cutoffUtc,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var entries = db.StockEntries.AsNoTracking()
                .Where(e => e.LocationType == InventoryLocationType.Outlet   // <-- FIX (enum compare)
                         && e.LocationId == outletId);

            if (cutoffUtc.HasValue)
                entries = entries.Where(e => e.Ts <= cutoffUtc.Value);

            var onHandDict = await entries
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    OnHand = Math.Round(g.Sum(x => x.QtyChange), 3)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.OnHand, ct);

            var items = await db.Items.AsNoTracking()
                .Select(i => new
                {
                    i.Id,
                    i.Sku,
                    DisplayName = i.Name,
                    Brand = i.Brand != null ? i.Brand.Name : "",
                    Category = i.Category != null ? i.Category.Name : "",
                    UnitPrice = i.Price           // decimal (non-nullable)  <-- FIX (no ??)
                })
                .ToListAsync(ct);

            var rows = items
                .Where(i => onHandDict.TryGetValue(i.Id, out var qty) && qty != 0m)
                .Select(i =>
                {
                    var qty = onHandDict[i.Id];
                    var price = Math.Round(i.UnitPrice, 2, MidpointRounding.AwayFromZero);

                    return new StockValuationRow
                    {
                        Sku = i.Sku ?? "",
                        DisplayName = i.DisplayName ?? "",
                        Brand = i.Brand ?? "",
                        Category = i.Category ?? "",
                        OnHand = qty,
                        UnitCost = 0m,
                        UnitPrice = price,
                    };
                })
                .OrderBy(r => r.DisplayName)
                .ToList();

            return rows;
        }
    }
}
