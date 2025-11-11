// Pos.Persistence/Services/ReturnsService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Sales;
using Pos.Domain.Pricing;
using Pos.Domain.Services;
using Pos.Persistence.Sync;   // ← add this

namespace Pos.Persistence.Services
{
    public class ReturnsService : IReturnsService
    {
        private readonly PosClientDbContext _db;
        private readonly IOutboxWriter _outbox;   // ← add

        public ReturnsService(PosClientDbContext db, IOutboxWriter outbox) // ← inject
        {
            _db = db;
            _outbox = outbox;
        }
        /// <summary>
        /// Create a finalized Sale (IsReturn = true) without linking to an original invoice.
        /// - Validates lines
        /// - Computes totals (no tax logic here)
        /// - Writes stock entries to add stock back to the outlet
        /// - Assigns a new return invoice number (SR series via local helper)
        /// NOTE: No GL posting here (Client layer triggers it).
        /// </summary>
        public async Task<int> CreateReturnWithoutInvoiceAsync(
            int outletId,
            int counterId,
            int? tillSessionId,
            int userId,
            IEnumerable<ReturnNoInvLine> lines,
            int? customerId = null,
            string? customerName = null,
            string? customerPhone = null,
            string? reason = null)
        {
            var list = (lines ?? Enumerable.Empty<ReturnNoInvLine>())
                       .Where(l => l.ItemId > 0 && l.Qty > 0m)
                       .ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("Add at least one item.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            // 1) Return header (FINAL)
            var nowUtc = DateTime.UtcNow;
            var ret = new Sale
            {
                Ts = nowUtc,
                OutletId = outletId,
                CounterId = counterId,
                TillSessionId = tillSessionId,

                IsReturn = true,
                OriginalSaleId = null,
                Revision = 0,
                RevisedFromSaleId = null,
                RevisedToSaleId = null,

                Status = SaleStatus.Final,
                HoldTag = null,

                CustomerKind = customerId.HasValue ? CustomerKind.Registered : CustomerKind.WalkIn,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,

                Note = reason,

                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash
            };

            // 2) Totals (tax = 0 for this path)
            decimal subtotal = 0m;
            foreach (var l in list)
            {
                var net = (l.UnitPrice - l.Discount) * l.Qty;
                subtotal += net;
            }

            ret.Subtotal = subtotal;
            ret.TaxTotal = 0m;
            ret.Total = ret.Subtotal + ret.TaxTotal;

            // immediate refund recorded on Sale (GL will credit Till later from client)
            ret.CashAmount = ret.Total;
            ret.CardAmount = 0m;

            // 3) SR numbering (local helper)
            ret.InvoiceNumber = await NextReturnInvoiceNumber(counterId);

            // 4) Save header
            _db.Sales.Add(ret);
            await _db.SaveChangesAsync();

            // 5) Stock back to outlet
            foreach (var l in list)
            {
                _db.StockEntries.Add(new StockEntry
                {
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = outletId,
                    OutletId = outletId,
                    ItemId = l.ItemId,
                    QtyChange = (int)l.Qty
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // === SYNC: enqueue finalized Sale Return (IsReturn = true) inside the same TX ===
            await _outbox.EnqueueUpsertAsync(_db, ret, default);  // record the return document
            await _db.SaveChangesAsync();                          // persist the outbox row
            return ret.Id;
        }

        // Project-local numbering helper
        private async Task<int> NextReturnInvoiceNumber(int counterId)
        {
            var last = await _db.Sales
                .Where(s => s.CounterId == counterId && s.IsReturn)
                .OrderByDescending(s => s.InvoiceNumber)
                .Select(s => s.InvoiceNumber)
                .FirstOrDefaultAsync();

            return last + 1;
        }

        public async Task<EditReturnLoadDto> LoadReturnForAmendAsync(int returnSaleId, CancellationToken ct = default)
        {
            var ret = await _db.Sales.AsNoTracking().FirstAsync(s => s.Id == returnSaleId, ct);
            if (!ret.IsReturn) throw new InvalidOperationException("Not a return.");
            if (ret.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL returns can be amended.");
            var originalSaleId = ret.OriginalSaleId ?? 0;

            var oldLines = await _db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == returnSaleId)
                .ToListAsync(ct);

            var soldByItem = await _db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == originalSaleId)
                .GroupBy(l => l.ItemId)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

            var returnedByOthers = await (
                from s in _db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == originalSaleId && s.Status != SaleStatus.Voided && s.Id != returnSaleId
                join l in _db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
            ).ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct);

            var items = await _db.Items.AsNoTracking()
                .Select(i => new { i.Id, i.Sku, i.Name })
                .ToDictionaryAsync(i => i.Id, i => new { i.Sku, i.Name }, ct);

            var lines = new List<EditReturnLineDto>();
            foreach (var l in oldLines)
            {
                var sold = soldByItem.TryGetValue(l.ItemId, out var sQty) ? sQty : 0;
                var oldRetAbs = Math.Abs(l.Qty);
                var otherReturned = returnedByOthers.TryGetValue(l.ItemId, out var rQty) ? rQty : 0;
                var availableNow = Math.Max(0, sold - otherReturned);
                var meta = items.TryGetValue(l.ItemId, out var m) ? m : new { Sku = "", Name = "" };

                lines.Add(new EditReturnLineDto(
                    l.ItemId,
                    meta.Sku ?? "",
                    meta.Name ?? "",
                    sold,
                    otherReturned,
                    oldRetAbs,
                    availableNow,
                    l.UnitPrice,
                    l.DiscountPct,
                    l.DiscountAmt,
                    l.TaxRatePct,
                    l.TaxInclusive
                ));
            }

            return new EditReturnLoadDto(
                ReturnSaleId: returnSaleId,
                OriginalSaleId: originalSaleId,
                CounterId: ret.CounterId,
                InvoiceNumber: ret.InvoiceNumber,
                Revision: ret.Revision,
                CurrentTotal: ret.Total,
                Lines: lines
            );
        }

        public async Task<EditReturnFinalizeResult> FinalizeReturnAmendAsync(EditReturnFinalizeRequest req, CancellationToken ct = default)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var latest = await _db.Sales
                .Where(s => s.Id == req.ReturnSaleId)
                .Select(s => new { s.CounterId, s.InvoiceNumber, s.IsReturn, s.Status })
                .FirstAsync(ct);

            if (!latest.IsReturn) throw new InvalidOperationException("Not a return.");
            if (latest.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL returns can be amended.");

            var current = await _db.Sales
                .Where(s => s.CounterId == latest.CounterId
                         && s.InvoiceNumber == latest.InvoiceNumber
                         && s.IsReturn
                         && s.Status != SaleStatus.Voided)
                .OrderByDescending(s => s.Revision)
                .FirstAsync(ct);

            var originalSaleId = current.OriginalSaleId ?? 0;

            var originalSoldByItem = await _db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == originalSaleId)
                .GroupBy(l => l.ItemId)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

            var returnedByOthers = await (
                from s in _db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == originalSaleId && s.Status != SaleStatus.Voided && s.Id != current.Id
                join l in _db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
            ).ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct);

            // validate capacity per item
            foreach (var x in req.Lines)
            {
                var sold = originalSoldByItem.TryGetValue(x.ItemId, out var sQty) ? sQty : 0;
                var others = returnedByOthers.TryGetValue(x.ItemId, out var oQty) ? oQty : 0;
                var availNow = Math.Max(0, sold - others);
                if (x.ReturnQty > availNow)
                    throw new InvalidOperationException($"Item {x.ItemId} requested {x.ReturnQty}, available {availNow}.");
            }

            var latestLines = await _db.SaleLines.Where(l => l.SaleId == current.Id).ToListAsync(ct);

            // compute new magnitudes
            var calcs = req.Lines.Select(r => PricingMath.CalcLine(new LineInput(
                Qty: (int)r.ReturnQty,
                UnitPrice: r.UnitPrice,
                DiscountPct: r.DiscountPct,
                DiscountAmt: r.DiscountAmt,
                TaxRatePct: r.TaxRatePct,
                TaxInclusive: r.TaxInclusive
            ))).ToList();

            var magSubtotal = calcs.Sum(a => a.LineNet);
            var magTax = calcs.Sum(a => a.LineTax);
            var magGrand = magSubtotal + magTax;

            var amended = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = current.OutletId,
                CounterId = current.CounterId,
                TillSessionId = current.TillSessionId,
                IsReturn = true,
                OriginalSaleId = originalSaleId,
                Status = SaleStatus.Final,
                Revision = current.Revision + 1,
                RevisedFromSaleId = current.Id,
                InvoiceNumber = current.InvoiceNumber,

                Subtotal = -magSubtotal,
                TaxTotal = -magTax,
                Total = -magGrand,

                CashierId = current.CashierId,
                SalesmanId = current.SalesmanId,

                CustomerKind = current.CustomerKind,
                CustomerId = current.CustomerId,
                CustomerName = current.CustomerName,
                CustomerPhone = current.CustomerPhone,

                Note = req.Reason,
                EReceiptToken = Guid.NewGuid().ToString("N"),
                EReceiptUrl = null,
                InvoiceFooter = current.InvoiceFooter
            };
            _db.Sales.Add(amended);
            await _db.SaveChangesAsync(ct);

            foreach (var r in req.Lines)
            {
                var calc = PricingMath.CalcLine(new LineInput(
                    Qty: (int)r.ReturnQty,
                    UnitPrice: r.UnitPrice,
                    DiscountPct: r.DiscountPct,
                    DiscountAmt: r.DiscountAmt,
                    TaxRatePct: r.TaxRatePct,
                    TaxInclusive: r.TaxInclusive));

                _db.SaleLines.Add(new SaleLine
                {
                    SaleId = amended.Id,
                    ItemId = r.ItemId,
                    Qty = -r.ReturnQty,
                    UnitPrice = r.UnitPrice,
                    DiscountPct = r.DiscountPct,
                    DiscountAmt = r.DiscountAmt,
                    TaxCode = null,
                    TaxRatePct = r.TaxRatePct,
                    TaxInclusive = r.TaxInclusive,

                    UnitNet = -calc.UnitNet,
                    LineNet = -calc.LineNet,
                    LineTax = -calc.LineTax,
                    LineTotal = -calc.LineTotal
                });
            }
            await _db.SaveChangesAsync(ct);

            current.Status = SaleStatus.Revised;
            current.RevisedToSaleId = amended.Id;
            await _db.SaveChangesAsync(ct);

            // stock delta
            var oldByItem = latestLines.ToDictionary(x => x.ItemId, x => x);
            var newByItem = req.Lines.ToDictionary(x => x.ItemId, x => x);

            foreach (var itemId in oldByItem.Keys.Union(newByItem.Keys).Distinct())
            {
                var oldQty = oldByItem.TryGetValue(itemId, out var o) ? o.Qty : 0;                // negative
                var newQty = newByItem.TryGetValue(itemId, out var nr) ? -nr.ReturnQty : 0;       // negative
                var deltaQty = newQty - oldQty;
                if (deltaQty != 0)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = amended.OutletId,
                        OutletId = amended.OutletId,
                        ItemId = itemId,
                        QtyChange = -deltaQty,
                        RefType = "Amend",
                        RefId = amended.Id,
                        Ts = DateTime.UtcNow
                    });
                }
            }
            await _db.SaveChangesAsync(ct);

            // signed payment delta (UI already split & signed)
            var amountDelta = amended.Total - current.Total; // (+collect / -refund)
            if (amountDelta != 0m)
            {
                amended.CashAmount = req.PayCash;
                amended.CardAmount = req.PayCard;
                amended.PaymentMethod =
                    (req.PayCash != 0 && req.PayCard != 0) ? PaymentMethod.Mixed :
                    (req.PayCash != 0) ? PaymentMethod.Cash : PaymentMethod.Card;

                await _db.SaveChangesAsync(ct);
            }

            var deltaSub = amended.Subtotal - current.Subtotal;
            var deltaTax = amended.TaxTotal - current.TaxTotal;

            await tx.CommitAsync(ct);

            // enqueue for sync (finalized amended return)
            await _outbox.EnqueueUpsertAsync(_db, amended, ct);
            await _db.SaveChangesAsync(ct);

            return new EditReturnFinalizeResult(
                AmendedSaleId: amended.Id,
                NewRevision: amended.Revision,
                DeltaSubtotal: deltaSub,
                DeltaTax: deltaTax,
                NewTotal: amended.Total
            );
        }
    }
}
