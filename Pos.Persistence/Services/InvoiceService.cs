// Pos.Persistence/Services/InvoiceService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Formatting;
using Pos.Domain.Models.Sales;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class InvoiceService : IInvoiceService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public InvoiceService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<IReadOnlyList<InvoiceSearchRowDto>> SearchLatestInvoicesAsync(
            int outletId, int counterId, DateTime? fromUtc, DateTime? toUtc, string? search, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Latest revision per (OutletId, CounterId, InvoiceNumber)
            var maxRev = db.Sales.AsNoTracking()
                .GroupBy(s => new { s.OutletId, s.CounterId, s.InvoiceNumber })
                .Select(g => new
                {
                    g.Key.OutletId,
                    g.Key.CounterId,
                    g.Key.InvoiceNumber,
                    Rev = g.Max(x => x.Revision)
                });

            var q =
                from s in db.Sales.AsNoTracking()
                join m in maxRev
                    on new { s.OutletId, s.CounterId, s.InvoiceNumber, s.Revision }
                    equals new { m.OutletId, m.CounterId, m.InvoiceNumber, Revision = m.Rev }
                where s.OutletId == outletId && s.CounterId == counterId
                select s;

            if (fromUtc.HasValue) q = q.Where(s => s.Ts >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(s => s.Ts < toUtc.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                if (int.TryParse(text, out var invNo))
                {
                    q = q.Where(s => s.InvoiceNumber == invNo);
                }
                else
                {
                    q = q.Where(s =>
                        (s.CustomerName != null && EF.Functions.Like(s.CustomerName, $"%{text}%")) ||
                        (s.CustomerPhone != null && EF.Functions.Like(s.CustomerPhone, $"%{text}%")));
                }
            }

            var rows = await q.OrderByDescending(s => s.Ts)
                .Take(500)
                .Select(s => new InvoiceSearchRowDto(
                    s.Id,
                    s.CounterId,
                    s.InvoiceNumber,
                    s.Revision,
                    s.Status,
                    s.IsReturn,
                    s.Ts,
                    s.CustomerName ?? (s.CustomerPhone ?? "Walk-in"),
                    s.Total))
                .ToListAsync(ct);

            return rows;
        }

        public async Task<Dictionary<int, bool>> GetReturnHasBaseMapAsync(
            IEnumerable<int> returnSaleIds, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var ids = returnSaleIds.Distinct().ToList();
            if (ids.Count == 0) return new();

            return await db.Sales.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, HasBase = (s.RefSaleId != null) || (s.OriginalSaleId != null) })
                .ToDictionaryAsync(x => x.Id, x => x.HasBase, ct);
        }

        public async Task<(InvoiceDetailHeaderDto header, IReadOnlyList<InvoiceDetailLineDto> lines)>
            LoadSaleWithLinesAsync(int saleId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var sale = await db.Sales.AsNoTracking().FirstAsync(s => s.Id == saleId, ct);

            var linesRaw = await (
                from l in db.SaleLines.AsNoTracking().Where(x => x.SaleId == saleId)
                join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                from p in gp.DefaultIfEmpty()
                select new
                {
                    l.ItemId,
                    i.Sku,
                    ItemName = i.Name,
                    ProductName = p != null ? p.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    l.Qty,
                    l.UnitPrice,
                    l.LineTotal
                }
            ).ToListAsync(ct);

            var lines = linesRaw.Select(x =>
                new InvoiceDetailLineDto(
                    x.ItemId,
                    x.Sku ?? "",
                    ProductNameComposer.Compose(x.ProductName, x.ItemName, x.Variant1Name, x.Variant1Value, x.Variant2Name, x.Variant2Value),
                    x.Qty,
                    x.UnitPrice,
                    x.LineTotal
                )).ToList();

            var header = new InvoiceDetailHeaderDto(
                sale.Id, sale.CounterId, sale.InvoiceNumber, sale.Revision, sale.Status,
                sale.IsReturn, sale.Ts, sale.Total);

            return (header, lines);
        }

        public async Task<bool> AnyHeldAsync(int outletId, int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Sales.AsNoTracking()
                .AnyAsync(s => s.OutletId == outletId && s.CounterId == counterId && s.Status == SaleStatus.Draft, ct);
        }

        public async Task<bool> HasNonVoidedReturnAgainstAsync(int saleId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Sales.AsNoTracking()
                .AnyAsync(s => s.IsReturn
                            && (s.OriginalSaleId == saleId || s.RefSaleId == saleId)
                            && s.Status != SaleStatus.Voided, ct);
        }

        public async Task VoidReturnAsync(int saleId, string reason, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var sale = await db.Sales.FirstAsync(s => s.Id == saleId, ct);
            if (!sale.IsReturn) throw new InvalidOperationException("Not a return.");
            if (sale.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL returns can be voided.");

            var prior = await db.StockEntries
                .Where(e => e.RefId == sale.Id && e.RefType == "SaleReturn")
                .ToListAsync(ct);

            foreach (var p in prior)
            {
                db.StockEntries.Add(new StockEntry
                {
                    LocationType = p.LocationType,
                    LocationId = p.LocationId,
                    OutletId = p.OutletId,
                    ItemId = p.ItemId,
                    QtyChange = -p.QtyChange, // exact negation
                    RefType = "Void",
                    RefId = sale.Id,
                    Ts = DateTime.UtcNow
                });
            }

            sale.Status = SaleStatus.Voided;
            sale.VoidReason = reason;
            sale.VoidedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task VoidSaleAsync(int saleId, string reason, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var hasReturn = await db.Sales.AsNoTracking()
                .AnyAsync(s => s.IsReturn
                           && (s.OriginalSaleId == saleId || s.RefSaleId == saleId)
                           && s.Status != SaleStatus.Voided, ct);
            if (hasReturn) throw new InvalidOperationException("This sale has a return against it and cannot be voided.");

            var sale = await db.Sales.FirstAsync(s => s.Id == saleId, ct);
            if (sale.IsReturn) throw new InvalidOperationException("Selected document is a return.");
            if (sale.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL invoices can be voided.");

            // Reverse stock for each line
            var lines = await db.SaleLines.Where(l => l.SaleId == sale.Id).ToListAsync(ct);
            foreach (var l in lines)
            {
                db.StockEntries.Add(new StockEntry
                {
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = sale.OutletId,
                    OutletId = sale.OutletId,
                    ItemId = l.ItemId,
                    QtyChange = +l.Qty, // reverse sale OUT -> IN
                    RefType = "Void",
                    RefId = sale.Id,
                    Ts = DateTime.UtcNow
                });
            }

            // Mark sale void
            sale.Status = SaleStatus.Voided;
            sale.VoidReason = reason;
            sale.VoidedAtUtc = DateTime.UtcNow;

            // 🔻 NEW: Inactivate ALL effective GL rows for this sale's chain
            // ChainId is Guid (sale.PublicId); IsEffective is bool
            var glRows = await db.GlEntries
                .Where(e => e.ChainId == sale.PublicId && e.IsEffective)
                .ToListAsync(ct);

            if (glRows.Count > 0)
            {
                foreach (var r in glRows)
                    r.IsEffective = false;
            }
            // 🔺 END NEW

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }


        public async Task<IReadOnlyList<HeldRowDto>> GetHeldAsync(int outletId, int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Sales.AsNoTracking()
                .Where(s => s.OutletId == outletId
                         && s.CounterId == counterId
                         && s.Status == SaleStatus.Draft)
                .OrderByDescending(s => s.Ts)
                .Select(s => new HeldRowDto(
                    s.Id,
                    s.Ts,
                    s.HoldTag,
                    s.CustomerName,
                    s.Total))
                .ToListAsync(ct);
        }

        public async Task DeleteHeldAsync(int saleId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var s = await db.Sales.FirstOrDefaultAsync(x => x.Id == saleId && x.Status == SaleStatus.Draft, ct);
            if (s == null) return;

            var lines = db.SaleLines.Where(x => x.SaleId == s.Id);
            db.SaleLines.RemoveRange(lines);
            db.Sales.Remove(s);
            await db.SaveChangesAsync(ct);
        }
    }
}
