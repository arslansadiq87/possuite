// Pos.Persistence/Services/PurchasesService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    public class PurchasesService
    {
        private readonly PosClientDbContext _db;
        public PurchasesService(PosClientDbContext db) => _db = db;

        // ---------- Public API ----------

        /// <summary>
        /// Create or update a DRAFT purchase (no stock postings).
        /// Replaces lines on update to keep things simple.
        /// </summary>
        public async Task<Purchase> SaveDraftAsync(Purchase draft, IEnumerable<PurchaseLine> lines, string? user = null)
        {
            var lineList = NormalizeAndCompute(lines);

            // (re)compute header totals
            ComputeHeaderTotals(draft, lineList);

            draft.Status = PurchaseStatus.Draft;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            draft.UpdatedBy = user;

            if (draft.Id == 0)
            {
                draft.CreatedAtUtc = DateTime.UtcNow;
                draft.CreatedBy = user;
                draft.Lines = lineList;
                _db.Purchases.Add(draft);
            }
            else
            {
                // Load + replace lines
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == draft.Id);

                // update header fields you allow in Draft
                existing.SupplierId = draft.SupplierId;
                existing.TargetType = draft.TargetType;     // Outlet/Warehouse
                existing.OutletId = draft.OutletId;
                existing.WarehouseId = draft.WarehouseId;
                existing.PurchaseDate = draft.PurchaseDate;
                existing.VendorInvoiceNo = draft.VendorInvoiceNo;
                existing.DocNo = draft.DocNo;

                existing.Subtotal = draft.Subtotal;
                existing.Discount = draft.Discount;
                existing.Tax = draft.Tax;
                existing.OtherCharges = draft.OtherCharges;
                existing.GrandTotal = draft.GrandTotal;

                existing.Status = PurchaseStatus.Draft;
                existing.UpdatedAtUtc = draft.UpdatedAtUtc;
                existing.UpdatedBy = draft.UpdatedBy;

                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync(); // ensure deletes are flushed before insert
                existing.Lines = lineList;

                draft = existing; // return the tracked instance
            }

            await _db.SaveChangesAsync();
            return draft;
        }

        /// <summary>
        /// Finalize (Receive) a purchase: sets Status=Final, stamps ReceivedAtUtc,
        /// replaces lines, recomputes totals. (Stock ledger posting comes next step.)
        /// </summary>
        public async Task<Purchase> ReceiveAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null)
        {
            ValidateDestination(model);

            var lineList = NormalizeAndCompute(lines);
            ComputeHeaderTotals(model, lineList);

            model.Status = PurchaseStatus.Final;
            model.ReceivedAtUtc ??= DateTime.UtcNow;
            model.UpdatedAtUtc = DateTime.UtcNow;
            model.UpdatedBy = user;

            if (model.Id == 0)
            {
                model.CreatedAtUtc = DateTime.UtcNow;
                model.CreatedBy = user;
                model.Lines = lineList;
                _db.Purchases.Add(model);
            }
            else
            {
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id);

                // update all allowed header fields for Finalization
                existing.SupplierId = model.SupplierId;
                existing.TargetType = model.TargetType;
                existing.OutletId = model.OutletId;
                existing.WarehouseId = model.WarehouseId;
                existing.PurchaseDate = model.PurchaseDate;
                existing.VendorInvoiceNo = model.VendorInvoiceNo;
                existing.DocNo = model.DocNo;

                existing.Subtotal = model.Subtotal;
                existing.Discount = model.Discount;
                existing.Tax = model.Tax;
                existing.OtherCharges = model.OtherCharges;
                existing.GrandTotal = model.GrandTotal;

                existing.Status = PurchaseStatus.Final;
                existing.ReceivedAtUtc = model.ReceivedAtUtc;
                existing.UpdatedAtUtc = model.UpdatedAtUtc;
                existing.UpdatedBy = model.UpdatedBy;

                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync();
                existing.Lines = lineList;

                model = existing;
            }

            await _db.SaveChangesAsync();

            // NOTE: Stock ledger posting (StockEntry) will be added in the next step,
            // once we align with your exact StockEntry fields.

            return model;
        }

        /// <summary>
        /// Auto-pick last UnitCost/Discount/TaxRate from latest FINAL purchase line of this item.
        /// </summary>
        public async Task<(decimal unitCost, decimal discount, decimal taxRate)?> GetLastPurchaseDefaultsAsync(int itemId)
        {
            var last = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.ItemId == itemId && _db.Purchases
                    .Where(p => p.Id == x.PurchaseId && p.Status == PurchaseStatus.Final)
                    .Any())
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.UnitCost, x.Discount, x.TaxRate })
                .FirstOrDefaultAsync();

            if (last == null) return null;
            return (last.UnitCost, last.Discount, last.TaxRate);
        }

        // (Optional convenience) add/record a payment against a purchase
        public async Task<PurchasePayment> AddPaymentAsync(
            int purchaseId,
            PurchasePaymentKind kind,
            TenderMethod method,
            decimal amount,
            string? note,
            int outletId,
            int supplierId,
            int? tillSessionId,
            int? counterId,
            string user)
                {
            if (amount <= 0) throw new InvalidOperationException("Amount must be > 0");

            var purchase = await _db.Purchases.Include(p => p.Payments).FirstAsync(p => p.Id == purchaseId);
            if (purchase.Status == PurchaseStatus.Voided) throw new InvalidOperationException("Cannot pay voided purchase");

            var currentPaid = purchase.Payments.Sum(p => p.Amount);
            if (currentPaid + amount > purchase.GrandTotal)
                throw new InvalidOperationException("Payment exceeds total.");

            var pay = new PurchasePayment
            {
                PurchaseId = purchaseId,
                SupplierId = supplierId,
                OutletId = outletId,
                TsUtc = DateTime.UtcNow,
                Kind = kind,
                Method = method,
                Amount = Math.Round(amount, 2),
                Note = note,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user
            };

            _db.PurchasePayments.Add(pay);
            await _db.SaveChangesAsync();

            if (method == TenderMethod.Cash)
            {
                var cash = new CashLedger
                {
                    OutletId = outletId,
                    CounterId = counterId,
                    TillSessionId = tillSessionId,
                    TsUtc = DateTime.UtcNow,
                    Delta = -pay.Amount,
                    RefType = "PurchasePayment",
                    RefId = pay.Id,
                    Note = note,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = user
                };
                _db.CashLedgers.Add(cash);
                await _db.SaveChangesAsync();
            }

            purchase.CashPaid = purchase.Payments.Sum(p => p.Amount);
            purchase.CreditDue = Math.Max(0, purchase.GrandTotal - purchase.CashPaid);
            await _db.SaveChangesAsync();

            return pay;
        }

        public async Task<(Purchase purchase, List<PurchasePayment> payments)> GetWithPaymentsAsync(int purchaseId)
        {
            var purchase = await _db.Purchases.FirstAsync(p => p.Id == purchaseId);
            var pays = await _db.PurchasePayments.Where(x => x.PurchaseId == purchaseId).OrderBy(x => x.TsUtc).ToListAsync();
            return (purchase, pays);
        }

        public async Task<Purchase> FinalizeReceiveAsync(
            Purchase purchase,
            IEnumerable<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> onReceivePayments,
            int outletId,
            int supplierId,
            int? tillSessionId,
            int? counterId,
            string user)
        {
            var model = await ReceiveAsync(purchase, lines, user);

            foreach (var p in onReceivePayments)
            {
                await AddPaymentAsync(model.Id, PurchasePaymentKind.OnReceive, p.method, p.amount, p.note,
                    outletId, supplierId, tillSessionId, counterId, user);
            }

            return model;
        }

        // ---------- Helpers ----------

        private static List<PurchaseLine> NormalizeAndCompute(IEnumerable<PurchaseLine> lines)
        {
            var list = lines.ToList();
            foreach (var l in list)
            {
                // coerce negatives to zero where it makes sense
                l.Qty = l.Qty < 0 ? 0 : l.Qty;
                l.UnitCost = l.UnitCost < 0 ? 0 : l.UnitCost;
                l.Discount = l.Discount < 0 ? 0 : l.Discount;
                l.TaxRate = l.TaxRate < 0 ? 0 : l.TaxRate;

                var baseAmt = l.Qty * l.UnitCost;
                var taxable = Math.Max(0, baseAmt - l.Discount);
                var tax = Math.Round(taxable * (l.TaxRate / 100m), 2);
                l.LineTotal = Math.Round(taxable + tax, 2);
            }
            return list;
        }

        private static void ComputeHeaderTotals(Purchase p, IReadOnlyCollection<PurchaseLine> lines)
        {
            p.Subtotal = Math.Round(lines.Sum(x => x.Qty * x.UnitCost), 2);
            p.Discount = Math.Round(lines.Sum(x => x.Discount), 2);
            p.Tax = Math.Round(lines.Sum(x =>
                                 Math.Max(0, x.Qty * x.UnitCost - x.Discount) * (x.TaxRate / 100m)), 2);
            // keep p.OtherCharges as set by caller (UI)
            p.GrandTotal = Math.Round(p.Subtotal - p.Discount + p.Tax + p.OtherCharges, 2);

            // keep these snapshots consistent if present
            try
            {
                p.CashPaid = Math.Min(p.CashPaid, p.GrandTotal);
                p.CreditDue = Math.Max(0, p.GrandTotal - p.CashPaid);
            }
            catch { /* ignore if fields not present */ }
        }

        private static void ValidateDestination(Purchase p)
        {
            if (p.SupplierId <= 0)
                throw new InvalidOperationException("Supplier required.");

            if (p.TargetType == StockTargetType.Outlet)
            {
                if (p.OutletId is null || p.OutletId <= 0)
                    throw new InvalidOperationException("Outlet required.");
            }
            else if (p.TargetType == StockTargetType.Warehouse)
            {
                if (p.WarehouseId is null || p.WarehouseId <= 0)
                    throw new InvalidOperationException("Warehouse required.");
            }
        }
    }
}
