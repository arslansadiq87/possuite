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
            // 1) Validate original
            var orig = await _db.Sales.FirstAsync(s => s.Id == req.OriginalSaleId, ct);
            if (orig.IsReturn) throw new InvalidOperationException("Returns cannot be amended here.");
            if (orig.Status != SaleStatus.Final) throw new InvalidOperationException("Only FINAL invoices can be amended.");

            // Block editing the original when a return exists
            if (await HasActiveReturnAsync(req.OriginalSaleId, ct))
                throw new InvalidOperationException("This invoice has a posted return; you cannot amend the original. Amend the return instead.");

            // 2) Totals deltas
            var deltaSub = req.Subtotal - orig.Subtotal;
            var deltaTax = req.TaxTotal - orig.TaxTotal;
            var deltaGrand = req.Total - orig.Total;
            if (deltaGrand < -0.005m)
                throw new InvalidOperationException("The amended total is LOWER than the original. Use 'Return (with invoice)'.");

            // 3) Build qty maps for stock deltas
            var origLines = await _db.SaleLines.Where(l => l.SaleId == orig.Id).ToListAsync(ct);
            var origQtyByItem = origLines.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var newQtyByItem = req.Lines.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var allItemIds = origQtyByItem.Keys.Union(newQtyByItem.Keys).Distinct().ToList();

            // 4) Prepare stock deltas for guard and for SaleRev entries
            var pendingOutDeltas = new List<StockDeltaDto>();
            var pendingEntries = new List<StockEntry>();

            foreach (var itemId in allItemIds)
            {
                var oldQty = origQtyByItem.TryGetValue(itemId, out var oq) ? oq : 0m;
                var newQty = newQtyByItem.TryGetValue(itemId, out var nq) ? nq : 0m;
                var deltaQty = newQty - oldQty; // +ve = extra sold (OUT), -ve = IN

                if (deltaQty > 0m)
                {
                    // Guard OUT
                    pendingOutDeltas.Add(new StockDeltaDto(
                        ItemId: itemId,
                        OutletId: req.OutletId,
                        LocType: InventoryLocationType.Outlet,
                        LocId: req.OutletId,
                        Delta: -deltaQty));

                    // Prepare OUT entry (UnitCost set later)
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
                    // Prepare IN entry (UnitCost set later)
                    pendingEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = req.OutletId,
                        OutletId = req.OutletId,
                        ItemId = itemId,
                        QtyChange = qtyIn,    // IN
                        RefType = "SaleRev",
                        RefId = 0,         // backfilled later
                        Ts = DateTime.UtcNow
                    });
                }
            }

            // 5) Guard once for all positive OUT
            if (pendingOutDeltas.Count > 0)
                await _guard.EnsureNoNegativeAtLocationAsync(pendingOutDeltas, atUtc: null, ct);

            // 6) Start TX: save revised sale, lines, and SaleRev stock entries
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

                // NOTE: keep these as DELTAS for the GL delta poster
                CashAmount = req.CollectedCash,
                CardAmount = req.CollectedCard,
                PaymentMethod = (deltaGrand > 0.005m) ? req.PaymentMethod : orig.PaymentMethod,

                InvoiceFooter = req.InvoiceFooter
            };
            _db.Sales.Add(newSale);
            await _db.SaveChangesAsync(ct);

            // Lines (⚠ ensure Qty int)
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

            // 7) Build original issue cost per item (weighted) from the original sale's OUT entries
            var origIssueCostByItem = await _db.StockEntries.AsNoTracking()
                .Where(e => e.RefType == "Sale" && e.RefId == orig.Id && e.QtyChange < 0) // OUT
                .GroupBy(e => e.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    UnitCost = g.Sum(x => (-x.QtyChange) * x.UnitCost) / g.Sum(x => -x.QtyChange)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.UnitCost, ct);

            // 8) Backfill RefId & set UnitCost for SaleRev entries
            var nowUtc = DateTime.UtcNow;
            foreach (var se in pendingEntries)
            {
                se.RefId = newSale.Id;
                se.Ts = nowUtc;

                if (se.QtyChange < 0) // OUT (extra sold in amendment): use current moving-average cost
                {
                    se.UnitCost = await _in.GetMovingAverageCostAsync(
                        se.ItemId, InventoryLocationType.Outlet, req.OutletId, nowUtc, ct);

                    if (se.UnitCost <= 0m)
                        throw new InvalidOperationException($"No cost available for ItemId={se.ItemId} (amend OUT).");
                }
                else // IN (returned in amendment): use original sale's issue cost; fallback to moving-average
                {
                    if (!origIssueCostByItem.TryGetValue(se.ItemId, out var origCost) || origCost <= 0m)
                    {
                        origCost = await _in.GetMovingAverageCostAsync(
                            se.ItemId, InventoryLocationType.Outlet, req.OutletId, nowUtc, ct);
                    }
                    se.UnitCost = origCost;
                }
            }
            _db.StockEntries.AddRange(pendingEntries);

            // 9) Link original → revised
            var origTracked = await _db.Sales.FirstAsync(s => s.Id == orig.Id, ct);
            origTracked.RevisedToSaleId = newSale.Id;

            // 10) Outbox BEFORE final save + commit (house rule)
            await _outbox.EnqueueUpsertAsync(_db, newSale, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // 11) GL revision (delta-only) — posts Revenue/Tax Δ, sign-aware Receipts/AR Δ, and COGS/Inventory Δ (from SaleRev costs)
            var already = await _db.GlEntries.AsNoTracking()
                .AnyAsync(g => g.DocType == Pos.Domain.Accounting.GlDocType.SaleRevision && g.DocId == newSale.Id, ct);
            if (!already)
                await _gl.PostSaleRevisionAsync(newSale, deltaSub, deltaTax);

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
