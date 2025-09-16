// Pos.Persistence/Services/InvoiceService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    public sealed class InvoiceService
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;
        public InvoiceService(DbContextOptions<PosClientDbContext> opts) => _opts = opts;

        // Lightweight row for the browser grid
        public sealed class InvoiceRow
        {
            public int SaleId { get; set; }
            public int CounterId { get; set; }
            public int InvoiceNumber { get; set; }
            public int Revision { get; set; }
            public SaleStatus Status { get; set; }
            public bool IsReturn { get; set; }
            public DateTime TsUtc { get; set; }
            public string Customer { get; set; } = "";
            public decimal Total { get; set; }
        }

        public IList<InvoiceRow> SearchLatestInvoices(
            int? outletId, int? counterId,
            DateTime? fromUtc, DateTime? toUtc,
            string? text // matches invoice no / customer / phone
        )
        {
            using var db = new PosClientDbContext(_opts);

            // Max revision per (CounterId, InvoiceNumber), excluding Voided
            var maxRev = db.Sales.AsNoTracking()
                .Where(s => s.Status != SaleStatus.Voided)
                .GroupBy(s => new { s.CounterId, s.InvoiceNumber })
                .Select(g => new { g.Key.CounterId, g.Key.InvoiceNumber, Rev = g.Max(x => x.Revision) });

            var q =
                from s in db.Sales.AsNoTracking()
                join m in maxRev
                    on new { s.CounterId, s.InvoiceNumber, Revision = s.Revision }
                    equals new { m.CounterId, m.InvoiceNumber, Revision = m.Rev }
                select s;


            if (outletId.HasValue) q = q.Where(s => s.OutletId == outletId.Value);
            if (counterId.HasValue) q = q.Where(s => s.CounterId == counterId.Value);
            if (fromUtc.HasValue) q = q.Where(s => s.Ts >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(s => s.Ts < toUtc.Value);

            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();
                if (int.TryParse(text, out var invNo))
                    q = q.Where(s => s.InvoiceNumber == invNo);
                else
                    q = q.Where(s =>
                        (s.CustomerName != null && EF.Functions.Like(s.CustomerName, $"%{text}%")) ||
                        (s.CustomerPhone != null && EF.Functions.Like(s.CustomerPhone, $"%{text}%")));
            }

            return q
                .OrderByDescending(s => s.Ts)
                .Take(500)
                .Select(s => new InvoiceRow
                {
                    SaleId = s.Id,
                    CounterId = s.CounterId,
                    InvoiceNumber = s.InvoiceNumber,
                    Revision = s.Revision,
                    Status = s.Status,
                    IsReturn = s.IsReturn,
                    TsUtc = s.Ts,
                    Customer = s.CustomerName ?? (s.CustomerPhone ?? "Walk-in"),
                    Total = s.Total
                })
                .ToList();
        }

        public (Sale sale, List<(SaleLine line, string itemName, string sku)> lines) LoadSaleWithLines(int saleId)
        {
            using var db = new PosClientDbContext(_opts);
            var sale = db.Sales.AsNoTracking().First(x => x.Id == saleId);

            var q =
            from l in db.SaleLines.AsNoTracking().Where(x => x.SaleId == saleId)
            join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
            select new { Line = l, Name = i.Name, Sku = i.Sku };

            // First project to a concrete class or anonymous type, then ToList, THEN tuples:
            var materialized = q
                .Select(x => new
                {
                    Line = x.Line,
                    ItemName = x.Name ?? "",
                    Sku = x.Sku ?? ""
                })
                .ToList();

            var lines = materialized
                .Select(x => (x.Line, x.ItemName, x.Sku))
                .ToList();

            return (sale, lines);
        }
    }
}
