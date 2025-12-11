// Pos.Client.Wpf/Services/PurchaseCenterReadService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Formatting;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IPurchaseCenterReadService
    {
        Task<IReadOnlyList<PurchaseRowDto>> SearchAsync(
            DateTime? fromUtc, DateTime? toUtc, string? term,
            bool wantFinal, bool wantDraft, bool wantVoided, bool onlyWithDoc);

        Task<IReadOnlyList<LineRowDto>> GetPreviewLinesAsync(int purchaseId);

        Task<bool> AnyHeldDraftAsync();
        Task<(bool HasActiveReturns, bool IsReturnWithInvoice)> GetPreviewActionGuardsAsync(
    int purchaseId, CancellationToken ct = default);
        // IPurchasesReadService (or your read service interface)
     

    }

    public sealed class PurchaseRowDto
    {
        public int PurchaseId { get; init; }
        public string DocNoOrId { get; init; } = "";
        public string Supplier { get; init; } = "";
        public string TsLocal { get; init; } = "";
        public string Status { get; init; } = "";
        public decimal GrandTotal { get; init; }
        public bool IsReturn { get; init; }
        public int Revision { get; init; }
        public bool HasRevisions => Revision > 1;
        public bool IsReturnWithInvoice { get; init; }
    }

    public sealed class LineRowDto
    {
        public int ItemId { get; init; }
        public string Sku { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public decimal Qty { get; init; }
        public decimal UnitCost { get; init; }
        public decimal LineTotal { get; init; }
    }

    public sealed class PurchaseCenterReadService : IPurchaseCenterReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public PurchaseCenterReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<IReadOnlyList<PurchaseRowDto>> SearchAsync(
    DateTime? fromUtc, DateTime? toUtc, string? term,
    bool wantFinal, bool wantDraft, bool wantVoided, bool onlyWithDoc)
        {
            await using var db = await _dbf.CreateDbContextAsync();

            term = (term ?? string.Empty).Trim();
            var hasTerm = !string.IsNullOrWhiteSpace(term);
            var termLower = hasTerm ? term.ToLower() : string.Empty;

            // -------- 1) Collect matching purchase IDs when a term is present --------
            List<int>? matchedIds = null;

            if (hasTerm)
            {
                // Common date predicate
                var baseDateQuery = db.Purchases.AsNoTracking()
                    .Where(p =>
                        (!fromUtc.HasValue || (p.CreatedAtUtc >= fromUtc || p.ReceivedAtUtc >= fromUtc)) &&
                        (!toUtc.HasValue || (p.CreatedAtUtc < toUtc || p.ReceivedAtUtc < toUtc)));

                // (a) Header-level matches: DocNo / VendorInvoiceNo / Supplier (case-insensitive)
                var headerIdsQuery = baseDateQuery
                    .Include(p => p.Party)
                    .Where(p =>
                        ((p.DocNo ?? string.Empty).ToLower().Contains(termLower)) ||
                        ((p.VendorInvoiceNo ?? string.Empty).ToLower().Contains(termLower)) ||
                        (p.Party != null &&
                         (p.Party.Name ?? string.Empty).ToLower().Contains(termLower)))
                    .Select(p => p.Id);

                // (b) Item-level matches: item/product/SKU (case-insensitive)
                var itemIdsQuery =
                    from p in baseDateQuery
                    from l in p.Lines
                    join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                    join pr in db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                    from pr in gp.DefaultIfEmpty()
                    where
                        (!string.IsNullOrEmpty(i.Name) &&
                         i.Name.ToLower().Contains(termLower)) ||
                        (pr != null &&
                         !string.IsNullOrEmpty(pr.Name) &&
                         pr.Name.ToLower().Contains(termLower)) ||
                        (!string.IsNullOrEmpty(i.Sku) &&
                         i.Sku.ToLower().Contains(termLower))
                    select p.Id;

                matchedIds = await headerIdsQuery
                    .Union(itemIdsQuery)
                    .Distinct()
                    .ToListAsync();

                if (matchedIds.Count == 0)
                    return Array.Empty<PurchaseRowDto>();
            }

            // -------- 2) Main purchases query in date range --------
            var q = db.Purchases.AsNoTracking()
                .Include(p => p.Party)
                .Where(p =>
                    (!fromUtc.HasValue || (p.CreatedAtUtc >= fromUtc || p.ReceivedAtUtc >= fromUtc)) &&
                    (!toUtc.HasValue || (p.CreatedAtUtc < toUtc || p.ReceivedAtUtc < toUtc)));

            if (hasTerm && matchedIds != null)
            {
                q = q.Where(p => matchedIds.Contains(p.Id));
            }

            // -------- 3) Projection + status/doc filters --------
            var list = await q.OrderByDescending(p => p.ReceivedAtUtc ?? p.CreatedAtUtc)
                .Select(p => new
                {
                    p.Id,
                    p.DocNo,
                    Supplier = p.Party != null ? p.Party.Name : "",
                    Ts = p.ReceivedAtUtc ?? p.CreatedAtUtc,
                    p.Status,
                    p.GrandTotal,
                    p.Revision,
                    p.IsReturn,
                    p.RefPurchaseId
                })
                .ToListAsync();

            var filtered = list
                .Where(r =>
                    ((r.Status == PurchaseStatus.Final && wantFinal) ||
                     (r.Status == PurchaseStatus.Draft && wantDraft) ||
                     (r.Status == PurchaseStatus.Voided && wantVoided))
                    && (!onlyWithDoc || !string.IsNullOrWhiteSpace(r.DocNo)))
                .Select(r => new PurchaseRowDto
                {
                    PurchaseId = r.Id,
                    DocNoOrId = string.IsNullOrWhiteSpace(r.DocNo) ? $"#{r.Id}" : r.DocNo!,
                    Supplier = string.IsNullOrWhiteSpace(r.Supplier) ? "—" : r.Supplier.Trim(),
                    TsLocal = r.Ts.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                    Status = r.Status.ToString(),
                    GrandTotal = r.IsReturn ? -Math.Abs(r.GrandTotal) : Math.Abs(r.GrandTotal),
                    Revision = r.Revision,
                    IsReturn = r.IsReturn,
                    IsReturnWithInvoice = r.IsReturn && r.RefPurchaseId.HasValue
                })
                .ToList();

            return filtered;
        }


        // Implementation alongside GetPreviewLinesAsync
        public async Task<(bool HasActiveReturns, bool IsReturnWithInvoice)> GetPreviewActionGuardsAsync(
            int purchaseId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var p = await db.Purchases.AsNoTracking()
                .Select(x => new { x.Id, x.IsReturn, x.RefPurchaseId, x.Status })
                .FirstAsync(x => x.Id == purchaseId, ct);

            bool hasActiveReturns = false;
            if (!p.IsReturn)
            {
                hasActiveReturns = await db.Purchases.AsNoTracking()
                    .AnyAsync(r => r.IsReturn
                                && r.RefPurchaseId == p.Id
                                && r.Status != PurchaseStatus.Voided, ct);
            }

            bool isReturnWithInvoice = p.IsReturn && p.RefPurchaseId != null;
            return (hasActiveReturns, isReturnWithInvoice);
        }



        public async Task<IReadOnlyList<LineRowDto>> GetPreviewLinesAsync(int purchaseId)
        {
            await using var db = await _dbf.CreateDbContextAsync();

            var purchase = await db.Purchases.AsNoTracking()
                .Include(p => p.Lines)
                .FirstAsync(p => p.Id == purchaseId);

            var isReturn = purchase.IsReturn;

            // amendments deltas
            var refTypeAmend = isReturn ? "PurchaseReturnAmend" : "PurchaseAmend";

            var amendQtyByItem = await db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == refTypeAmend && se.RefId == purchase.Id)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

            var baseByItem = purchase.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Qty = g.Sum(x => x.Qty),
                        AvgUnitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m
                    });

            var itemIds = baseByItem.Keys.Union(amendQtyByItem.Keys).Distinct().ToList();

            var meta = await (
                from i in db.Items.AsNoTracking()
                join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                from p in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = p != null ? p.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku,
                    i.Price
                }
            ).ToListAsync();

            var metaLookup = meta.ToDictionary(x => x.Id);

            var rows = new List<LineRowDto>();
            foreach (var itemId in itemIds)
            {
                baseByItem.TryGetValue(itemId, out var b);
                amendQtyByItem.TryGetValue(itemId, out var aQty);

                var effectiveQty = (b?.Qty ?? 0m) + aQty;
                var displayQty = isReturn ? Math.Abs(effectiveQty) : effectiveQty;
                if (displayQty == 0m) continue;

                var unitCost = b?.AvgUnitCost ?? (metaLookup.TryGetValue(itemId, out var m0) ? (m0?.Price ?? 0m) : 0m);

                string display = $"Item #{itemId}";
                string sku = "";
                if (metaLookup.TryGetValue(itemId, out var m))
                {
                    display = ProductNameComposer.Compose(
                        m.ProductName, m.ItemName,
                        m.Variant1Name, m.Variant1Value,
                        m.Variant2Name, m.Variant2Value);
                    sku = m.Sku ?? "";
                }

                rows.Add(new LineRowDto
                {
                    ItemId = itemId,
                    Sku = sku,
                    DisplayName = display,
                    Qty = displayQty,
                    UnitCost = unitCost,
                    LineTotal = Math.Round(displayQty * unitCost, 2)
                });
            }
            return rows;
        }

        public async Task<bool> AnyHeldDraftAsync()
        {
            await using var db = await _dbf.CreateDbContextAsync();
            return await db.Purchases.AsNoTracking()
                .AnyAsync(p => p.Status == PurchaseStatus.Draft && !p.IsReturn);
        }
    }
}
