using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.DTO;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    /// <summary>Single source of truth for Till-session summaries.</summary>
    public sealed class TillReadService : ITillReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public TillReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<TillSessionSummaryDto> GetSessionSummaryAsync(int tillId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var till = await db.TillSessions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tillId, ct);

            if (till == null)
                throw new InvalidOperationException($"Till session {tillId} not found.");

            // --- Latest surviving docs (exclude voided & superseded)
            var latestBase = db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == tillId
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null
                         && s.RevisedToSaleId == null);

            var latest = await latestBase.ToListAsync(ct);

            var latestSales = latest.Where(s => !s.IsReturn).ToList();
            var latestReturns = latest.Where(s => s.IsReturn).ToList();

            var salesTotal = latestSales.Sum(s => s.Total);
            var returnsTotalAbs = latestReturns.Sum(s => Math.Abs(s.Total));
            var taxCollected = latestSales.Sum(s => s.TaxTotal);
            var taxRefundedAbs = latestReturns.Sum(s => Math.Abs(s.TaxTotal));

            var salesCount = latestSales.Count;
            var returnsCount = latestReturns.Count;
            var docsCount = latest.Count;

            // Items from latest docs
            decimal itemsSoldQtyDec = 0m, itemsRetQtyDec = 0m;

            var saleIds = latestSales.Select(s => s.Id).ToHashSet();
            var returnIds = latestReturns.Select(s => s.Id).ToHashSet();

            if (saleIds.Count > 0)
            {
                var soldList = await db.SaleLines.AsNoTracking()
                    .Where(l => saleIds.Contains(l.SaleId))
                    .Select(l => l.Qty)
                    .ToListAsync(ct);
                itemsSoldQtyDec = soldList.Sum(q => (decimal)q);
            }

            if (returnIds.Count > 0)
            {
                var retList = await db.SaleLines.AsNoTracking()
                    .Where(l => returnIds.Contains(l.SaleId))
                    .Select(l => l.Qty)
                    .ToListAsync(ct);
                itemsRetQtyDec = Math.Abs(retList.Sum(q => (decimal)q));
            }

            // --- Movements across ALL non-voided revisions (including superseded)
            var moves = await db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == tillId
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null)
                .ToListAsync(ct);

            var openingFloat = till.OpeningFloat;
            var salesCash = moves.Where(s => !s.IsReturn).Sum(s => s.CashAmount);
            var refundsCashAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CashAmount));
            var salesCard = moves.Where(s => !s.IsReturn).Sum(s => s.CardAmount);
            var refundsCardAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CardAmount));

            // Expected cash: opening + signed net cash (returns subtract)
            var expectedCash = openingFloat + salesCash - refundsCashAbs;

            var lastTxUtc = moves.OrderByDescending(s => s.Ts).FirstOrDefault()?.Ts;

            // --- Amendments & voids (session-scoped)
            var salesAmendments = await db.Sales.AsNoTracking().CountAsync(s =>
                    s.TillSessionId == tillId &&
                    s.RevisedToSaleId != null &&
                    s.Status == SaleStatus.Final &&
                    s.VoidedAtUtc == null &&
                    !s.IsReturn, ct);

            var returnAmendments = await db.Sales.AsNoTracking().CountAsync(s =>
                    s.TillSessionId == tillId &&
                    s.RevisedToSaleId != null &&
                    s.Status == SaleStatus.Final &&
                    s.VoidedAtUtc == null &&
                    s.IsReturn, ct);

            var voidsCount = await db.Sales.AsNoTracking().CountAsync(s =>
                    s.TillSessionId == tillId &&
                    s.VoidedAtUtc != null, ct);

            return new TillSessionSummaryDto
            {
                TillId = till.Id,
                OutletId = till.OutletId,
                CounterId = till.CounterId,
                OpenedAtUtc = till.OpenTs,
                LastTxUtc = lastTxUtc,

                SalesCount = salesCount,
                ReturnsCount = returnsCount,
                DocsCount = docsCount,

                ItemsSoldQty = itemsSoldQtyDec,
                ItemsReturnedQty = itemsRetQtyDec,
                ItemsNetQty = itemsSoldQtyDec - itemsRetQtyDec,

                SalesTotal = salesTotal,
                ReturnsTotalAbs = returnsTotalAbs,

                TaxCollected = taxCollected,
                TaxRefundedAbs = taxRefundedAbs,

                OpeningFloat = openingFloat,
                SalesCash = salesCash,
                RefundsCashAbs = refundsCashAbs,
                ExpectedCash = expectedCash,

                SalesCard = salesCard,
                RefundsCardAbs = refundsCardAbs,

                SalesAmendments = salesAmendments,
                ReturnAmendments = returnAmendments,
                VoidsCount = voidsCount
            };
        }
    }
}
