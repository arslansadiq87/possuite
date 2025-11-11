using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Sales;
using Pos.Domain.Services;
using Pos.Persistence.Sync;
using Pos.Domain.Formatting;
using Pos.Domain.Accounting;
using Pos.Domain.Models.Inventory;


namespace Pos.Persistence.Services
{
    public partial class SalesService : ISalesService
    {
        private readonly PosClientDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly IGlPostingService _gl;
        private readonly IStockGuard _guard;

        public SalesService(PosClientDbContext db, IOutboxWriter outbox, IGlPostingService gl, IStockGuard guard)
        {
            _db = db;
            _outbox = outbox;
            _gl = gl;
            _guard = guard;
        }

        // ---------- Reads ----------
        public async Task<IReadOnlyList<ItemIndexDto>> GetItemIndexAsync(CancellationToken ct = default)
        {
            // mirrors your LINQ from LoadItemIndex()
            var list = await (
                from i in _db.Items.AsNoTracking()
                join p in _db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                from p in gp.DefaultIfEmpty()
                let primaryBarcode = _db.ItemBarcodes
                    .Where(b => b.ItemId == i.Id && b.IsPrimary)
                    .Select(b => b.Code)
                    .FirstOrDefault()
                let anyBarcode = _db.ItemBarcodes
                    .Where(b => b.ItemId == i.Id)
                    .Select(b => b.Code)
                    .FirstOrDefault()
                orderby i.Name
                select new ItemIndexDto(
                    i.Id,
                    i.Name,
                    i.Sku,
                    primaryBarcode ?? anyBarcode ?? "",
                    i.Price,
                    i.TaxCode,
                    i.DefaultTaxRatePct,
                    i.TaxInclusive,
                    i.DefaultDiscountPct,
                    i.DefaultDiscountAmt,
                    p != null ? p.Name : null,
                    i.Variant1Name, i.Variant1Value,
                    i.Variant2Name, i.Variant2Value
                )).ToListAsync(ct);

            return list;
        }

        public async Task<IReadOnlyList<StaffLiteDto>> GetSalesmenAsync(CancellationToken ct = default)
        {
            var list = await _db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive && s.ActsAsSalesman)
                .OrderBy(s => s.FullName)
                .Select(s => new StaffLiteDto(s.Id, s.FullName))
                .ToListAsync(ct);

            // Insert "-- None --"
            var result = new List<StaffLiteDto> { new(0, "-- None --") };
            result.AddRange(list);
            return result;
        }

        public async Task<InvoicePreviewDto> GetInvoicePreviewAsync(int counterId, CancellationToken ct = default)
        {
            var seq = await _db.CounterSequences.SingleOrDefaultAsync(x => x.CounterId == counterId, ct);
            if (seq == null)
            {
                seq = new CounterSequence { CounterId = counterId, NextInvoiceNumber = 1 };
                _db.CounterSequences.Add(seq);
                await _db.SaveChangesAsync(ct);
            }
            return new InvoicePreviewDto(counterId, seq.NextInvoiceNumber);
        }

        public Task<TillSession?> GetOpenTillAsync(int outletId, int counterId, CancellationToken ct = default) =>
            _db.TillSessions.OrderByDescending(t => t.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.OutletId == outletId && t.CounterId == counterId && t.CloseTs == null, ct);

        public async Task<SaleResumeDto?> LoadHeldAsync(int saleId, CancellationToken ct = default)
        {
            var s = await _db.Sales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saleId && x.Status == SaleStatus.Draft, ct);
            if (s == null) return null;

            var lines = await _db.SaleLines.AsNoTracking().Where(x => x.SaleId == saleId).ToListAsync(ct);

            // Build SKU/DisplayName via item index joins
            var items = await _db.Items.AsNoTracking()
                .Where(i => lines.Select(l => l.ItemId).Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var prodIds = items.Values.Select(i => i.ProductId).Distinct().ToList();
            var prods = await _db.Products.AsNoTracking()
                .Where(p => prodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            string Compose(int itemId)
            {
                var i = items[itemId];
                var p = i.ProductId.HasValue && prods.TryGetValue(i.ProductId.Value, out var pp) ? pp.Name : null;
                return ProductNameComposer.Compose(p, i.Name, i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value);
            }

            var dto = new SaleResumeDto
            {
                SaleId = s.Id,
                IsReturn = s.IsReturn,
                InvoiceDiscountPct = s.InvoiceDiscountAmt.HasValue ? null : s.InvoiceDiscountPct,
                InvoiceDiscountAmt = s.InvoiceDiscountAmt,
                InvoiceFooter = s.InvoiceFooter,
                CustomerKind = s.CustomerKind,
                CustomerId = s.CustomerId,
                CustomerName = s.CustomerName,
                CustomerPhone = s.CustomerPhone,
                SalesmanId = s.SalesmanId
            };

            foreach (var l in lines)
            {
                var sku = items[l.ItemId].Sku ?? "";
                dto.Lines.Add(new SaleResumeDto.SaleLineRow(
                    l.ItemId, sku, Compose(l.ItemId), l.Qty, l.UnitPrice,
                    l.DiscountPct, l.DiscountAmt, l.TaxCode, l.TaxRatePct, l.TaxInclusive,
                    l.UnitNet, l.LineNet, l.LineTax, l.LineTotal));
            }
            return dto;
        }

        // ---------- Guards ----------
        public async Task<bool> GuardSaleQtyAsync(int outletId, int itemId, decimal proposedQty, CancellationToken ct = default)
        {
            if (proposedQty <= 0) return true;

            try
            {
                var deltas = new[]
                {
            new StockDeltaDto(
                ItemId:  itemId,
                OutletId: outletId,
                LocType: InventoryLocationType.Outlet,
                LocId:   outletId,
                Delta:  -proposedQty)
        };

                await _guard.EnsureNoNegativeAtLocationAsync(deltas, atUtc: null, ct);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }


        // ---------- Commands ----------
        public async Task<int> HoldAsync(SaleHoldRequest req, CancellationToken ct = default)
        {
            if (req.Total <= 0m) throw new InvalidOperationException("Total must be > 0.");

            var sale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = req.OutletId,
                CounterId = req.CounterId,
                TillSessionId = null,
                IsReturn = req.IsReturn,
                InvoiceNumber = 0,
                Status = SaleStatus.Draft,
                HoldTag = string.IsNullOrWhiteSpace(req.HoldTag) ? null : req.HoldTag!.Trim(),
                InvoiceDiscountPct = (req.InvoiceDiscountAmt.HasValue && req.InvoiceDiscountAmt > 0m) ? (decimal?)null : req.InvoiceDiscountPct,
                InvoiceDiscountAmt = (req.InvoiceDiscountAmt.HasValue && req.InvoiceDiscountAmt > 0m) ? req.InvoiceDiscountAmt : null,
                InvoiceDiscountValue = req.InvoiceDiscountValue,
                DiscountBeforeTax = true,
                Subtotal = req.Subtotal,
                TaxTotal = req.TaxTotal,
                Total = req.Total,
                CashierId = req.CashierId,
                SalesmanId = req.SalesmanId,
                CustomerKind = req.CustomerKind,
                CustomerId = req.CustomerId,
                CustomerName = req.CustomerName,
                CustomerPhone = req.CustomerPhone,
                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                EReceiptToken = null,
                EReceiptUrl = null,
                InvoiceFooter = req.Footer
            };
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(ct);

            foreach (var l in req.Lines)
            {
                _db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id,
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
            return sale.Id;
        }

        public async Task<Sale> FinalizeAsync(SaleFinalizeRequest req, CancellationToken ct = default)
        {
            if (req.Total <= 0m) throw new InvalidOperationException("Total must be > 0.");

            // Ensure open till exists (defensive)
            var open = await GetOpenTillAsync(req.OutletId, req.CounterId, ct);
            if (open == null || open.Id != req.TillSessionId)
                throw new InvalidOperationException("Till is closed.");

            //// Negative stock guard (aggregate cart)
            //var guard = new StockGuard(_db);
            //var deltas = req.Lines
            //    .GroupBy(l => l.ItemId)
            //    .Select(g => (itemId: g.Key,
            //                  outletId: req.OutletId,
            //                  locType: InventoryLocationType.Outlet,
            //                  locId: req.OutletId,
            //                  delta: -g.Sum(x => x.Qty)))
            //    .ToArray();
            //await guard.EnsureNoNegativeAtLocationAsync(deltas, atUtc: null, ct: ct);
            // Guard only when it’s an OUT flow (normal sale). Returns add stock.
            if (!req.IsReturn)
            {
                var deltas = req.Lines
                    .GroupBy(l => l.ItemId)
                    .Select(g => new StockDeltaDto(
                        ItemId: g.Key,
                        OutletId: req.OutletId,
                        LocType: InventoryLocationType.Outlet,
                        LocId: req.OutletId,
                        Delta: -g.Sum(x => x.Qty)))
                    .ToArray();

                await _guard.EnsureNoNegativeAtLocationAsync(deltas, atUtc: null, ct);
            }

            using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Allocate invoice number (inside TX)
            var seq = await _db.CounterSequences.SingleOrDefaultAsync(x => x.CounterId == req.CounterId, ct)
                      ?? _db.CounterSequences.Add(new CounterSequence { CounterId = req.CounterId, NextInvoiceNumber = 1 }).Entity;
            await _db.SaveChangesAsync(ct);
            var allocatedInvoiceNo = seq.NextInvoiceNumber;
            seq.NextInvoiceNumber++;
            await _db.SaveChangesAsync(ct);

            // Create sale
            var sale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = req.OutletId,
                CounterId = req.CounterId,
                TillSessionId = req.TillSessionId,
                Status = SaleStatus.Final,
                Revision = 0,
                RevisedFromSaleId = null,
                IsReturn = req.IsReturn,
                OriginalSaleId = req.OriginalSaleId,
                InvoiceNumber = allocatedInvoiceNo,
                InvoiceDiscountPct = (req.InvoiceDiscountAmt.HasValue && req.InvoiceDiscountAmt > 0m) ? (decimal?)null : req.InvoiceDiscountPct,
                InvoiceDiscountAmt = (req.InvoiceDiscountAmt.HasValue && req.InvoiceDiscountAmt > 0m) ? req.InvoiceDiscountAmt : null,
                InvoiceDiscountValue = req.InvoiceDiscountValue,
                DiscountBeforeTax = true,
                Subtotal = req.Subtotal,
                TaxTotal = req.TaxTotal,
                Total = req.Total,
                CashierId = req.CashierId,
                SalesmanId = req.SalesmanId,
                CustomerKind = req.CustomerKind,
                CustomerId = req.CustomerId,
                CustomerName = req.CustomerName,
                CustomerPhone = req.CustomerPhone,
                CashAmount = req.CashAmount,
                CardAmount = req.CardAmount,
                PaymentMethod = req.PaymentMethod,
                EReceiptToken = req.EReceiptToken,
                EReceiptUrl = null,
                InvoiceFooter = req.InvoiceFooter,
                Note = req.Note
                //InvoiceFooter = req.InvoiceFooter
            };
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(ct);

            // Lines & stock
            foreach (var l in req.Lines)
            {
                _db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id,
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

                _db.StockEntries.Add(new StockEntry
                {
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = req.OutletId,
                    OutletId = req.OutletId,
                    ItemId = l.ItemId,
                    //QtyChange = -l.Qty,
                    QtyChange = sale.IsReturn ? +l.Qty : -l.Qty,
                    UnitCost = 0m,
                    RefType = "Sale",
                    RefId = sale.Id,
                    StockDocId = null,
                    Ts = DateTime.UtcNow,
                    Note = null
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Outbox
            await _outbox.EnqueueUpsertAsync(_db, sale, ct);
            // GL post (best effort)
            try
            {
                if (!sale.IsReturn)
                    await _gl.PostSaleAsync(sale);
                else
                    await _gl.PostSaleReturnAsync(sale);
            }
            catch { /* swallow; log elsewhere */ }

            // Void held draft if any
            if (req.HeldSaleId.HasValue)
            {
                var draft = await _db.Sales.FirstOrDefaultAsync(x => x.Id == req.HeldSaleId.Value && x.Status == SaleStatus.Draft, ct);
                if (draft != null)
                {
                    draft.Status = SaleStatus.Voided;
                    draft.RevisedToSaleId = sale.Id;
                    draft.VoidedAtUtc = DateTime.UtcNow;
                    draft.VoidReason = "Finalized from held draft";
                    await _db.SaveChangesAsync(ct);
                }
            }

            // Optional: post to Party ledger (credit on unpaid part) — keep where you already do it, or inject PartyPostingService here if you prefer

            return sale;
        }

        // ---------- Existing (amend flow) ----------
        public async Task<int> AmendByReversalAndReissueAsync(int originalSaleId, int? tillSessionId, int userId, string? reason = null)
        {
            var original = await _db.Sales.FirstOrDefaultAsync(s => s.Id == originalSaleId);
            if (original == null)
                throw new InvalidOperationException($"Sale #{originalSaleId} not found.");

            if (original.IsReturn)
                throw new InvalidOperationException("Return invoices cannot be amended.");

            if (original.Status != SaleStatus.Final)
                throw new InvalidOperationException("Only Final sales can be amended.");

            original.Status = SaleStatus.Revised;
            original.Note = AppendNote(original.Note, $"Revised on {DateTime.UtcNow:u} by {userId}. {reason ?? ""}".Trim());

            var newSale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = original.OutletId,
                CounterId = original.CounterId,
                TillSessionId = null,
                IsReturn = false,
                OriginalSaleId = null,
                Revision = original.Revision + 1,
                RevisedFromSaleId = original.Id,
                RevisedToSaleId = null,
                Status = SaleStatus.Draft,
                HoldTag = null,
                CustomerName = original.CustomerName,
                Note = $"Amendment draft of #{original.InvoiceNumber} r{original.Revision}",
                InvoiceNumber = original.InvoiceNumber,
                InvoiceDiscountPct = original.InvoiceDiscountPct,
                InvoiceDiscountAmt = original.InvoiceDiscountAmt,
                InvoiceDiscountValue = original.InvoiceDiscountValue,
                DiscountBeforeTax = original.DiscountBeforeTax,
                Subtotal = 0m,
                TaxTotal = 0m,
                Total = 0m,
                CashierId = original.CashierId,
                SalesmanId = original.SalesmanId,
                CustomerKind = original.CustomerKind,
                CustomerId = original.CustomerId,
                CustomerPhone = original.CustomerPhone,
                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                EReceiptToken = null,
                EReceiptUrl = null,
                InvoiceFooter = original.InvoiceFooter
            };

            _db.Sales.Add(newSale);
            await _db.SaveChangesAsync();

            original.RevisedToSaleId = newSale.Id;
            await _db.SaveChangesAsync();

            await _outbox.EnqueueUpsertAsync(_db, newSale, default);
            await _db.SaveChangesAsync();

            return newSale.Id;
        }

        public async Task<ReturnFromInvoiceLoadDto> GetReturnFromInvoiceAsync(int saleId, CancellationToken ct = default)
        {
            var sale = await _db.Sales.AsNoTracking().FirstAsync(s => s.Id == saleId, ct);

            var lines = await (
                from l in _db.SaleLines.AsNoTracking().Where(x => x.SaleId == saleId)
                join i in _db.Items.AsNoTracking() on l.ItemId equals i.Id
                select new { l.ItemId, l.Qty, l.UnitPrice, l.DiscountPct, l.DiscountAmt, l.TaxRatePct, l.TaxInclusive, i.Sku, i.Name }
            ).ToListAsync(ct);

            var priorReturned = await (
                from s in _db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == saleId && s.Status != SaleStatus.Voided
                join l in _db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
            ).ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct);

            var dtoLines = lines.Select(x =>
            {
                var already = priorReturned.TryGetValue(x.ItemId, out var q) ? q : 0;
                var avail = Math.Max(0, x.Qty - already);
                return new ReturnFromInvoiceLineDto(
                    x.ItemId,
                    x.Sku ?? "",
                    x.Name ?? "",
                    x.Qty,
                    already,
                    avail,
                    x.UnitPrice,
                    x.DiscountPct,
                    x.DiscountAmt,
                    x.TaxRatePct,
                    x.TaxInclusive
                );
            }).ToList();

            var headerHuman = $"Invoice {sale.CounterId}-{sale.InvoiceNumber}  Rev {sale.Revision}  Total: {sale.Total:0.00}";
            //return new ReturnFromInvoiceLoadDto(sale.Id, sale.OutletId, sale.CounterId, sale.Revision, headerHuman, dtoLines);
            return new ReturnFromInvoiceLoadDto(sale.Id, sale.OutletId, sale.CounterId, sale.Revision, headerHuman, dtoLines);
        }


        private static string AppendNote(string? existing, string add)
            => string.IsNullOrWhiteSpace(existing) ? add : existing + Environment.NewLine + add;



    }
}
