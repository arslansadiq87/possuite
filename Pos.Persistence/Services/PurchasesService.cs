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

            ComputeHeaderTotals(draft, lineList);

            draft.Status = PurchaseStatus.Draft;
            draft.DocNo = null;                    // ⬅️ never number a Draft
            draft.ReceivedAtUtc = null;            // ⬅️ Drafts are not “received”
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
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == draft.Id);

                // header fields allowed in Draft
                existing.PartyId = draft.PartyId;
                existing.TargetType = draft.TargetType;
                existing.OutletId = draft.OutletId;
                existing.WarehouseId = draft.WarehouseId;
                existing.PurchaseDate = draft.PurchaseDate;
                existing.VendorInvoiceNo = draft.VendorInvoiceNo;

                existing.DocNo = null;
                existing.ReceivedAtUtc = null;

                existing.Subtotal = draft.Subtotal;
                existing.Discount = draft.Discount;
                existing.Tax = draft.Tax;
                existing.OtherCharges = draft.OtherCharges;
                existing.GrandTotal = draft.GrandTotal;

                existing.Status = PurchaseStatus.Draft;
                existing.UpdatedAtUtc = draft.UpdatedAtUtc;
                existing.UpdatedBy = draft.UpdatedBy;

                // clear old lines
                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync();

                // 🔑 re-attach new lines properly
                foreach (var l in lineList)
                {
                    l.Id = 0;                   // force EF to insert
                    l.PurchaseId = existing.Id; // make sure FK is set
                    l.Purchase = null;          // don’t carry over detached ref
                }
                existing.Lines = lineList;

                draft = existing;
            }


            await _db.SaveChangesAsync();
            return draft;
        }

        private async Task<string> EnsurePurchaseNumberAsync(Purchase p, CancellationToken ct)
        {
            // Simple safe fallback: if DocNo already set, keep it.
            if (!string.IsNullOrWhiteSpace(p.DocNo)) return p.DocNo!;

            // Minimal, local-only sequence: PO-YYYYMMDD-### (per day)
            var today = DateTime.UtcNow.Date;
            var prefix = $"PO-{today:yyyyMMdd}-";

            // Count existing Finals for today and +1 (works fine per-terminal/offline; replace with series later)
            var countToday = await _db.Purchases
                .AsNoTracking()
                .CountAsync(x => x.Status == PurchaseStatus.Final
                              && x.ReceivedAtUtc >= today
                              && x.ReceivedAtUtc < today.AddDays(1), ct);

            return prefix + (countToday + 1).ToString("D3");
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
                model.DocNo = await EnsurePurchaseNumberAsync(model, CancellationToken.None);   // ⬅️ assign number
                model.Lines = lineList;
                _db.Purchases.Add(model);
            }
            else
            {
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id);

                existing.PartyId = model.PartyId;
                existing.TargetType = model.TargetType;
                existing.OutletId = model.OutletId;
                existing.WarehouseId = model.WarehouseId;
                existing.PurchaseDate = model.PurchaseDate;
                existing.VendorInvoiceNo = model.VendorInvoiceNo;

                // Assign number if missing (allows pre-assigned numbers from UI if you want)
                existing.DocNo = string.IsNullOrWhiteSpace(model.DocNo)
                    ? await EnsurePurchaseNumberAsync(existing, CancellationToken.None)          // ⬅️ assign number
                    : model.DocNo;

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

                foreach (var l in lineList)
                {
                    l.Id = 0;
                    l.PurchaseId = existing.Id;
                    l.Purchase = null;
                }
                existing.Lines = lineList;
                
                model = existing;
            }

            await _db.SaveChangesAsync();

            // ⬇️ Stock ledger posting will hook in here later (Final only)
            // await _stock.PostPurchaseAsync(model, ct); (when you add it)

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
            // ----- Load & basic guards -----
            var purchase = await _db.Purchases
                .Include(p => p.Payments)
                .FirstAsync(p => p.Id == purchaseId);

            if (purchase.Status == PurchaseStatus.Voided)
                throw new InvalidOperationException("Cannot pay a voided purchase.");

            var amt = Math.Round(amount, 2);
            if (amt <= 0)
                throw new InvalidOperationException("Amount must be > 0.");

            // ----- Business rules by status -----
            // Draft (Held): only ADVANCE allowed (cash actually taken, but invoice not posted)
            // Final (Posted): ON_RECEIVE and ADJUSTMENT allowed; ADVANCE is not logical anymore
            switch (purchase.Status)
            {
                case PurchaseStatus.Draft:
                    if (kind != PurchasePaymentKind.Advance)
                        throw new InvalidOperationException("Only Advance payments are allowed on held (draft) purchases.");
                    break;

                case PurchaseStatus.Final:
                    if (kind == PurchasePaymentKind.Advance)
                        throw new InvalidOperationException("Use OnReceive or Adjustment for finalized purchases.");
                    break;

                default:
                    // Revised behaves like Final for payments, but keep it simple for now:
                    // If you want different behavior for Revised, branch here.
                    break;
            }

            // Prevent overpayment
            var currentPaid = purchase.Payments.Sum(p => p.Amount);
            if (currentPaid + amt > purchase.GrandTotal)
                throw new InvalidOperationException("Payment exceeds total.");

            // Ensure consistent Supplier/Outlet on the payment row
            var paySupplierId = purchase.PartyId; // trust the purchase
            int payOutletId;

            if (purchase.TargetType == StockTargetType.Outlet && purchase.OutletId is int po && po > 0)
            {
                payOutletId = po; // tie to the outlet that received stock
            }
            else
            {
                // For Warehouse-target purchases (or if outlet not on the purchase),
                // fall back to the outlet explicitly passed for the cash/till.
                if (outletId <= 0)
                    throw new InvalidOperationException("Outlet is required for recording the payment.");
                payOutletId = outletId;
            }

            // ----- Atomic write: payment + optional cash ledger + snapshots -----
            using var tx = await _db.Database.BeginTransactionAsync();

            var pay = new PurchasePayment
            {
                PurchaseId = purchase.Id,
                SupplierId = paySupplierId,
                OutletId = payOutletId,
                TsUtc = DateTime.UtcNow,
                Kind = kind,
                Method = method,
                Amount = amt,
                Note = note,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user
            };

            _db.PurchasePayments.Add(pay);
            await _db.SaveChangesAsync();

            // Cash ledger only for cash movements
            if (method == TenderMethod.Cash)
            {
                var cash = new CashLedger
                {
                    OutletId = payOutletId,
                    CounterId = counterId,
                    TillSessionId = tillSessionId,
                    TsUtc = DateTime.UtcNow,
                    Delta = -amt, // cash leaves the drawer to pay supplier
                    RefType = "PurchasePayment",
                    RefId = pay.Id,
                    Note = note,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = user
                };
                _db.CashLedgers.Add(cash);
                await _db.SaveChangesAsync();
            }

            // Refresh snapshots
            var newPaid = purchase.Payments.Sum(p => p.Amount); // includes the one we just added
            purchase.CashPaid = newPaid;
            purchase.CreditDue = Math.Max(0, purchase.GrandTotal - newPaid);
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
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
            if (p.PartyId <= 0)
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

        // ---------- Convenience Queries for UI Pickers ----------

        public Task<List<Purchase>> ListHeldAsync()
            => _db.Purchases
              .AsNoTracking()
              .Include(p => p.Party)   // <- keep this!
              .Where(p => p.Status == PurchaseStatus.Draft)
              .OrderByDescending(p => p.PurchaseDate)
              .ToListAsync();


        public Task<List<Purchase>> ListPostedAsync()
            => _db.Purchases
                  .AsNoTracking()
                  .Include(p => p.Party)
                  .Where(p => p.Status == PurchaseStatus.Final)
                  .OrderByDescending(p => p.ReceivedAtUtc ?? p.PurchaseDate)
                  .ToListAsync();

        public Task<Purchase> LoadWithLinesAsync(int id)
            => _db.Purchases
                  .Include(p => p.Lines)
                  .Include(p => p.Party)
                  .FirstAsync(p => p.Id == id);



    }
}
