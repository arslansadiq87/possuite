// Pos.Persistence/Services/SalesService.Edit.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Inventory;
using Pos.Domain.Models.Sales;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public partial class SalesService : ISalesService
    {
        public async Task<EditSaleLoadDto> GetSaleForEditAsync(int saleId)
        {
            var s = await _db.Sales.AsNoTracking().FirstAsync(x => x.Id == saleId);
            if (s.IsReturn) throw new InvalidOperationException("Returns cannot be amended here.");
            if (s.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL invoices can be amended.");

            var lines = await _db.SaleLines.AsNoTracking()
                            .Where(l => l.SaleId == s.Id)
                            .ToListAsync();

            return new EditSaleLoadDto(
                SaleId: s.Id,
                OutletId: s.OutletId,
                CounterId: s.CounterId,
                Revision: s.Revision,
                IsReturn: s.IsReturn,
                TsUtc: s.Ts,
                Subtotal: s.Subtotal,
                TaxTotal: s.TaxTotal,
                Total: s.Total,
                InvoiceDiscountPct: s.InvoiceDiscountPct,
                InvoiceDiscountAmt: s.InvoiceDiscountAmt,
                InvoiceDiscountValue: s.InvoiceDiscountValue,
                CustomerKind: s.CustomerKind,
                CustomerName: s.CustomerName,
                CustomerPhone: s.CustomerPhone,
                SalesmanId: s.SalesmanId,
                InvoiceFooter: s.InvoiceFooter,
                Lines: lines.Select(l => new EditSaleLoadDto.Line(
                    l.ItemId, l.Qty, l.UnitPrice, l.DiscountPct, l.DiscountAmt,
                    l.TaxCode, l.TaxRatePct, l.TaxInclusive, l.UnitNet, l.LineNet, l.LineTax, l.LineTotal
                )).ToList()
            );
        }

        public async Task<bool> GuardEditExtraOutAsync(int outletId, int itemId, decimal originalQty, decimal proposedCartQty, CancellationToken ct = default)
        {
            var extraOut = proposedCartQty - originalQty;
            if (extraOut <= 0m) return true;

            try
            {
                var deltas = new[]
                {
            new StockDeltaDto(
                ItemId:  itemId,
                OutletId: outletId,
                LocType: InventoryLocationType.Outlet,
                LocId:   outletId,
                Delta:  -extraOut)
        };

                await _guard.EnsureNoNegativeAtLocationAsync(deltas, atUtc: null, ct);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }


        public async Task<EditSaleSaveResult> SaveAmendmentAsync(EditSaleSaveRequest req, CancellationToken ct = default)
        {
            // Validate original
            var orig = await _db.Sales.FirstAsync(s => s.Id == req.OriginalSaleId, ct);
            if (orig.IsReturn) throw new InvalidOperationException("Returns cannot be amended here.");
            if (orig.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL invoices can be amended.");

            // Totals deltas
            var deltaSub = req.Subtotal - orig.Subtotal;
            var deltaTax = req.TaxTotal - orig.TaxTotal;
            var deltaGrand = req.Total - orig.Total;
            if (deltaGrand < -0.005m)
                throw new InvalidOperationException("The amended total is LOWER than the original. Use 'Return (with invoice)'.");

            // Build qty maps for stock deltas
            var origLines = await _db.SaleLines.Where(l => l.SaleId == orig.Id).ToListAsync(ct);
            var origQtyByItem = origLines.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var newQtyByItem = req.Lines.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var allItemIds = origQtyByItem.Keys.Union(newQtyByItem.Keys).Distinct().ToList();

            // Collect OUT deltas for guard and entries
            var pendingOutDeltas = new List<StockDeltaDto>();
            var pendingEntries = new List<StockEntry>();

            foreach (var itemId in allItemIds)
            {
                var oldQty = origQtyByItem.TryGetValue(itemId, out var oq) ? oq : 0m;
                var newQty = newQtyByItem.TryGetValue(itemId, out var nq) ? nq : 0m;
                var deltaQty = newQty - oldQty; // +ve = extra sold (OUT), -ve = IN

                if (deltaQty > 0m)
                {
                    pendingOutDeltas.Add(new StockDeltaDto(
                        ItemId: itemId,
                        OutletId: req.OutletId,
                        LocType: InventoryLocationType.Outlet,
                        LocId: req.OutletId,
                        Delta: -deltaQty));

                    pendingEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = req.OutletId,
                        OutletId = req.OutletId,
                        ItemId = itemId,
                        QtyChange = -deltaQty, // OUT
                        RefType = "SaleRev",
                        RefId = 0,         // backfilled later
                        Ts = DateTime.UtcNow
                    });
                }
                else if (deltaQty < 0m)
                {
                    var qtyIn = Math.Abs(deltaQty);
                    pendingEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = req.OutletId,
                        OutletId = req.OutletId,
                        ItemId = itemId,
                        QtyChange = qtyIn,     // IN
                        RefType = "SaleRev",
                        RefId = 0,         // backfilled later
                        Ts = DateTime.UtcNow
                    });
                }
            }

            // Guard once for all positive OUT
            if (pendingOutDeltas.Count > 0)
            {
                await _guard.EnsureNoNegativeAtLocationAsync(pendingOutDeltas, atUtc: null, ct);
            }

            // Save new revision (+ lines + stock) inside a TX
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var newSale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = req.OutletId,
                CounterId = req.CounterId,
                TillSessionId = req.TillSessionId,
                Status = SaleStatus.Final,
                IsReturn = false,
                InvoiceNumber = orig.InvoiceNumber,  // keep same number
                Revision = req.NewRevisionNumber,
                RevisedFromSaleId = orig.Id,
                RevisedToSaleId = null,

                InvoiceDiscountPct = req.InvoiceDiscountPct,
                InvoiceDiscountAmt = req.InvoiceDiscountAmt,
                InvoiceDiscountValue = req.InvoiceDiscountValue,
                DiscountBeforeTax = true,

                Subtotal = req.Subtotal,
                TaxTotal = req.TaxTotal,
                Total = req.Total,

                CashierId = req.CashierId,
                SalesmanId = req.SalesmanId,

                CustomerKind = req.CustomerKind,
                CustomerName = req.CustomerName,
                CustomerPhone = req.CustomerPhone,

                CashAmount = req.CollectedCash,
                CardAmount = req.CollectedCard,
                PaymentMethod = (deltaGrand > 0.005m) ? req.PaymentMethod : orig.PaymentMethod,

                InvoiceFooter = req.InvoiceFooter
            };
            _db.Sales.Add(newSale);
            await _db.SaveChangesAsync(ct);

            // Lines (⚠ ensure Qty type matches entity; your other methods cast to int)
            foreach (var l in req.Lines)
            {
                _db.SaleLines.Add(new SaleLine
                {
                    SaleId = newSale.Id,
                    ItemId = l.ItemId,
                    Qty = (int)decimal.Round(l.Qty, 0, MidpointRounding.AwayFromZero),
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    DiscountAmt = l.DiscountAmt,
                    TaxCode = l.TaxCode,
                    TaxRatePct = l.TaxRatePct,
                    TaxInclusive = l.TaxInclusive,
                    UnitNet = l.UnitNet,
                    LineNet = l.LineNet,
                    LineTax = l.LineTax,
                    LineTotal = l.LineTotal
                });
            }
            await _db.SaveChangesAsync(ct);

            // Stock entries RefId backfilled
            foreach (var se in pendingEntries) se.RefId = newSale.Id;
            _db.StockEntries.AddRange(pendingEntries);

            // link original → revised
            var origTracked = await _db.Sales.FirstAsync(s => s.Id == orig.Id, ct);
            origTracked.RevisedToSaleId = newSale.Id;

            // Outbox BEFORE final save + commit (house rule)
            await _outbox.EnqueueUpsertAsync(_db, newSale, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // GL revision (if not already)
            var already = await _db.GlEntries.AsNoTracking()
                .AnyAsync(g => g.DocType == Pos.Domain.Accounting.GlDocType.SaleRevision && g.DocId == newSale.Id, ct);
            if (!already)
                await _gl.PostSaleRevisionAsync(newSale, deltaSub, deltaTax); // keep signature as-is if it has no ct

            return new EditSaleSaveResult(
                NewSaleId: newSale.Id,
                NewRevision: newSale.Revision,
                DeltaSubtotal: deltaSub,
                DeltaTax: deltaTax,
                DeltaGrand: deltaGrand
            );
        }

    }
}
