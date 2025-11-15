// Pos.Persistence/Features/Transfers/TransferQueries.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;

namespace Pos.Persistence.Features.Transfers
{
    public sealed class TransferSearchFilter
    {
        public TransferStatus? Status { get; set; }            // null => Any
        public InventoryLocationType? FromType { get; set; }   // null => Any
        public int? FromId { get; set; }                       // null => Any
        public InventoryLocationType? ToType { get; set; }     // null => Any
        public int? ToId { get; set; }                         // null => Any
        public DateTime? DateFromUtc { get; set; }             // filter by Dispatch date (EffectiveDateUtc)
        public DateTime? DateToUtc { get; set; }               // exclusive
        public string? Search { get; set; }                    // TransferNo / SKU / Item name contains
        public int Skip { get; set; } = 0;
        public int Take { get; set; } = 50;                    // sane default
    }

    public sealed class TransferListRow
    {
        public int Id { get; set; }
        public string TransferNo { get; set; } = "";
        public TransferStatus Status { get; set; }
        public InventoryLocationType FromType { get; set; }
        public int FromId { get; set; }
        public string FromDisplay { get; set; } = "";
        public InventoryLocationType ToType { get; set; }
        public int ToId { get; set; }
        public string ToDisplay { get; set; } = "";
        public DateTime EffectiveDateUtc { get; set; }      // dispatch
        public DateTime? ReceivedAtUtc { get; set; }
        public int LineCount { get; set; }
        public decimal QtyExpectedSum { get; set; }
        public decimal QtyReceivedSum { get; set; }
        public string? FirstItem { get; set; }              // small UX nicety for quick glance
    }

    public interface ITransferQueries
    {
        Task<(IReadOnlyList<TransferListRow> Rows, int Total)> SearchAsync(TransferSearchFilter f, CancellationToken ct = default);
        Task<StockDoc?> GetAsync(int id, CancellationToken ct = default);
        Task<(StockDoc Doc, StockDocLine[] Lines)?> GetWithLinesAsync(int id, CancellationToken ct = default);
    }

    public sealed class TransferQueries : ITransferQueries
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public TransferQueries(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<(IReadOnlyList<TransferListRow>, int)> SearchAsync(TransferSearchFilter f, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.StockDocs.AsNoTracking()
                .Where(d => d.DocType == StockDocType.Transfer);

            if (f.Status.HasValue)
                q = q.Where(d => d.TransferStatus == f.Status.Value);

            if (f.FromType.HasValue) q = q.Where(d => d.LocationType == f.FromType.Value);
            if (f.FromId.HasValue) q = q.Where(d => d.LocationId == f.FromId.Value);
            if (f.ToType.HasValue) q = q.Where(d => d.ToLocationType == f.ToType.Value);
            if (f.ToId.HasValue) q = q.Where(d => d.ToLocationId == f.ToId.Value);

            if (f.DateFromUtc.HasValue) q = q.Where(d => d.EffectiveDateUtc >= f.DateFromUtc.Value);
            if (f.DateToUtc.HasValue) q = q.Where(d => d.EffectiveDateUtc < f.DateToUtc.Value);

            // Pre-join lines for aggregates + lightweight search
            // ---- Build filtered StockDocs query (no nested IQueryable in projection) ----
            var filt = q; // q already has non-search filters

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim();
                // Search by TransferNo OR any line's SKU/Item name
                filt = filt.Where(d =>
                    (d.TransferNo ?? "").Contains(s) ||
                    db.StockDocLines.Any(l => l.StockDocId == d.Id &&
                         ((l.SkuSnapshot ?? "").Contains(s) || (l.ItemNameSnapshot ?? "").Contains(s))));
            }

            var total = await filt.CountAsync(ct);

            // ---- Page and project with aggregate subqueries per doc ----
            var rows = await filt
                .OrderByDescending(d => d.EffectiveDateUtc)
                .ThenByDescending(d => d.Id)
                .Skip(f.Skip).Take(f.Take)
                .Select(d => new TransferListRow
                {
                    Id = d.Id,
                    TransferNo = d.TransferNo ?? "",
                    Status = d.TransferStatus ?? TransferStatus.Draft,
                    FromType = d.LocationType,
                    FromId = d.LocationId,
                    ToType = d.ToLocationType ?? d.LocationType,
                    ToId = d.ToLocationId ?? 0,
                    EffectiveDateUtc = d.EffectiveDateUtc,
                    ReceivedAtUtc = d.ReceivedAtUtc,

                    LineCount = db.StockDocLines.Count(l => l.StockDocId == d.Id),
                    QtyExpectedSum = db.StockDocLines.Where(l => l.StockDocId == d.Id).Sum(l => (decimal?)l.QtyExpected) ?? 0m,
                    QtyReceivedSum = db.StockDocLines.Where(l => l.StockDocId == d.Id).Sum(l => (decimal?)(l.QtyReceived ?? 0m)) ?? 0m,
                    FirstItem = db.StockDocLines.Where(l => l.StockDocId == d.Id)
                                .OrderBy(l => l.ItemNameSnapshot)
                                .Select(l => l.ItemNameSnapshot)
                                .FirstOrDefault()!
                })
                .ToListAsync(ct);


            // Resolve friendly From/To labels
            if (rows.Count > 0)
            {
                // batch fetch names
                var whIds = rows.Where(r => r.FromType == InventoryLocationType.Warehouse).Select(r => r.FromId)
                                .Concat(rows.Where(r => r.ToType == InventoryLocationType.Warehouse).Select(r => r.ToId))
                                .Distinct().ToList();
                var outIds = rows.Where(r => r.FromType == InventoryLocationType.Outlet).Select(r => r.FromId)
                                 .Concat(rows.Where(r => r.ToType == InventoryLocationType.Outlet).Select(r => r.ToId))
                                 .Distinct().ToList();

                var warehouses = await db.Warehouses.AsNoTracking()
                    .Where(w => whIds.Contains(w.Id))
                    .Select(w => new { w.Id, w.Code, w.Name })
                    .ToDictionaryAsync(x => x.Id, x => $"{(string.IsNullOrWhiteSpace(x.Code) ? $"W{x.Id}" : x.Code)} — {x.Name}", ct);

                var outlets = await db.Outlets.AsNoTracking()
                    .Where(o => outIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.Code, o.Name })
                    .ToDictionaryAsync(x => x.Id, x => $"{(string.IsNullOrWhiteSpace(x.Code) ? $"O{x.Id}" : x.Code)} — {x.Name}", ct);

                // Resolve friendly From/To labels
                foreach (var r in rows)
                {
                    // From
                    if (r.FromType == InventoryLocationType.Warehouse)
                    {
                        string? wName;
                        _ = warehouses.TryGetValue(r.FromId, out wName);
                        r.FromDisplay = wName ?? $"Warehouse #{r.FromId}";
                    }
                    else
                    {
                        string? oName;
                        _ = outlets.TryGetValue(r.FromId, out oName);
                        r.FromDisplay = oName ?? $"Outlet #{r.FromId}";
                    }

                    // To
                    if (r.ToType == InventoryLocationType.Warehouse)
                    {
                        string? wName;
                        _ = warehouses.TryGetValue(r.ToId, out wName);
                        r.ToDisplay = wName ?? $"Warehouse #{r.ToId}";
                    }
                    else
                    {
                        string? oName;
                        _ = outlets.TryGetValue(r.ToId, out oName);
                        r.ToDisplay = oName ?? $"Outlet #{r.ToId}";
                    }
                }


            }

            return (rows, total);
        }

        public async Task<StockDoc?> GetAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.StockDocs.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.DocType == StockDocType.Transfer, ct);
        }

        public async Task<(StockDoc Doc, StockDocLine[] Lines)?> GetWithLinesAsync(int id, CancellationToken ct = default)
        {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var doc = await db.StockDocs.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.DocType == StockDocType.Transfer, ct);
        if (doc is null) return null;
        var lines = await db.StockDocLines.AsNoTracking()
            .Where(l => l.StockDocId == id)
            .OrderBy(l => l.Id)
            .ToArrayAsync(ct);
        return (doc, lines);
        }
    }
}
