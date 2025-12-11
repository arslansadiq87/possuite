using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models.Reports;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class PurchaseLedgerReadService : IPurchaseLedgerReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public PurchaseLedgerReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        // Pos.Persistence/Services/PurchaseLedgerReadService.cs
        public async Task<IReadOnlyList<PurchaserLedgerRow>> GetSupplierLedgerAsync(
    DateTime? fromUtc, DateTime? toUtcExclusive, int? supplierId, int? outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.Purchases.AsNoTracking()
                .Include(p => p.Party)
                .Where(p => p.CreatedAtUtc >= fromUtc && p.CreatedAtUtc < toUtcExclusive);


            // NO date filter for now (per request)
            if (supplierId.HasValue) q = q.Where(p => p.PartyId == supplierId.Value);
            if (outletId.HasValue) q = q.Where(p => p.OutletId == outletId.Value);

            // Do NOT force Final status unless you actually mark it; uncomment later if needed.
            // q = q.Where(p => p.Status == PurchaseStatus.Final);

            var rows = await
                (
                    from p in db.Purchases.AsNoTracking()
                        // LEFT JOIN Parties
                    join pa in db.Parties.AsNoTracking()
                         on p.PartyId equals pa.Id into paj
                    from pa in paj.DefaultIfEmpty()

                        // same filters you already apply above:
                        // where p.CreatedAtUtc >= fromUtc && p.CreatedAtUtc < toUtcExclusive
                        // and (!supplierId.HasValue || p.PartyId == supplierId.Value)
                        // and (!outletId.HasValue   || p.OutletId == outletId.Value)

                    select new
                    {
                        p.Id,
                        p.DocNo,
                        Supplier = pa != null ? pa.Name : "",   // <-- guaranteed supplier name
                        CreatedAtUtc = p.CreatedAtUtc,
                        Total = p.GrandTotal
                    }
                )
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .ToListAsync(ct);




            var purchaseIds = rows.Select(r => r.Id).ToList();

            var paidLookup = await db.PurchasePayments.AsNoTracking()
                .Where(x => purchaseIds.Contains(x.PurchaseId))
                .GroupBy(x => x.PurchaseId)
                .Select(g => new { PurchaseId = g.Key, Paid = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.PurchaseId, x => x.Paid, ct);

            return rows.Select(r => new PurchaserLedgerRow
            {
                PurchaseId = r.Id,
                DocNo = string.IsNullOrWhiteSpace(r.DocNo) ? r.Id.ToString() : r.DocNo,
                Supplier = r.Supplier,
                TsUtc = r.CreatedAtUtc, // map to DTO's TsUtc to avoid changing your bindings
                GrandTotal = Math.Round(r.Total, 2, MidpointRounding.AwayFromZero),
                Paid = paidLookup.TryGetValue(r.Id, out var pv) ? Math.Round(pv, 2, MidpointRounding.AwayFromZero) : 0m
            })
            .ToList();
        }




    }
}
