// Pos.Persistence/Services/PurchasesService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    // NEW: lightweight DTO to carry refund instructions from UI
    public record SupplierRefundSpec(TenderMethod Method, decimal Amount, string? Note);

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
            // Drafts are *purchases*, not returns. We coerce negatives to zero.
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

            // Purchases (not returns) – prevent negative qtys
            var lineList = NormalizeAndCompute(lines);
            ComputeHeaderTotals(model, lineList);

            model.Status = PurchaseStatus.Final;
            model.ReceivedAtUtc ??= DateTime.UtcNow;
            model.UpdatedAtUtc = DateTime.UtcNow;
            model.UpdatedBy = user;

            if (model.Id == 0)
            {
                // First time finalization
                model.CreatedAtUtc = DateTime.UtcNow;
                model.CreatedBy = user;
                model.DocNo = await EnsurePurchaseNumberAsync(model, CancellationToken.None);
                model.Revision = 0;                              // NEW
                model.Lines = lineList;
                _db.Purchases.Add(model);
            }
            else
            {
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id);
                bool wasFinal = existing.Status == PurchaseStatus.Final;   // NEW

                // header updates...
                existing.PartyId = model.PartyId;
                existing.TargetType = model.TargetType;
                existing.OutletId = model.OutletId;
                existing.WarehouseId = model.WarehouseId;
                existing.PurchaseDate = model.PurchaseDate;
                existing.VendorInvoiceNo = model.VendorInvoiceNo;

                existing.DocNo = string.IsNullOrWhiteSpace(model.DocNo)
                    ? await EnsurePurchaseNumberAsync(existing, CancellationToken.None)
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

                // 🔁 NEW: bump revision if this is an amendment of a FINAL
                if (wasFinal)
                    existing.Revision = (existing.Revision <= 0 ? 1 : existing.Revision + 1);
                else
                    existing.Revision = 0; // first time becoming Final

                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync();

                foreach (var l in lineList)
                {
                    l.Id = 0; l.PurchaseId = existing.Id; l.Purchase = null;
                }
                existing.Lines = lineList;

                model = existing;
            }

            await _db.SaveChangesAsync();

            // NEW: After finalize, auto-apply any Supplier Credits before user records cash
            try
            {
                await ApplySupplierCreditsAsync(
                    supplierId: model.PartyId,
                    outletId: model.OutletId,
                    purchase: model,
                    user: user ?? "system"
                );
            }
            catch
            {
                // non-fatal
            }

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
            // 1) Finalize the purchase
            var model = await ReceiveAsync(purchase, lines, user);

            // 2) Auto-apply any Supplier Credits first
            try
            {
                await ApplySupplierCreditsAsync(
                    supplierId: model.PartyId,
                    outletId: model.OutletId,
                    purchase: model,
                    user: user ?? "system"
                );
            }
            catch
            {
                // non-fatal
            }

            // 3) Record OnReceive cash/card/bank payments
            foreach (var p in onReceivePayments)
            {
                await AddPaymentAsync(model.Id, PurchasePaymentKind.OnReceive, p.method, p.amount, p.note,
                    outletId, supplierId, tillSessionId, counterId, user);
            }

            return model;
        }

        // ---------- PURCHASE RETURNS (with refunds & supplier credit) ----------

        /// <summary>
        /// Build a draft for a return from a FINAL purchase.
        /// Pre-fills remaining-allowed quantities per line.
        /// </summary>
        public async Task<PurchaseReturnDraft> BuildReturnDraftAsync(int originalPurchaseId)
        {
            var p = await _db.Purchases
                .Include(x => x.Lines).ThenInclude(l => l.Item)
                .Include(x => x.Party)
                .FirstAsync(x => x.Id == originalPurchaseId && x.Status == PurchaseStatus.Final && !x.IsReturn);

            // Already returned per original line (stored as negative qty; convert to positive for math)
            var already = await _db.Purchases
                .Where(r => r.IsReturn && r.RefPurchaseId == originalPurchaseId && r.Status != PurchaseStatus.Voided)
                .SelectMany(r => r.Lines)
                .Where(l => l.RefPurchaseLineId != null)
                .GroupBy(l => l.RefPurchaseLineId!.Value)
                .Select(g => new { OriginalLineId = g.Key, ReturnedAbs = Math.Abs(g.Sum(z => z.Qty)) })
                .ToListAsync();

            var returnedByLine = already.ToDictionary(x => x.OriginalLineId, x => x.ReturnedAbs);

            return new PurchaseReturnDraft
            {
                PartyId = p.PartyId,
                TargetType = p.TargetType,
                OutletId = p.OutletId,
                WarehouseId = p.WarehouseId,
                RefPurchaseId = p.Id,
                Lines = p.Lines.Select(ol =>
                {
                    var done = returnedByLine.TryGetValue(ol.Id, out var r) ? r : 0m;
                    var remain = Math.Max(0, ol.Qty - done);

                    return new PurchaseReturnDraftLine
                    {
                        OriginalLineId = ol.Id,
                        ItemId = ol.ItemId,
                        ItemName = ol.Item?.Name ?? "",
                        UnitCost = ol.UnitCost,
                        MaxReturnQty = remain,
                        ReturnQty = remain
                    };
                })
                .Where(x => x.MaxReturnQty > 0)
                .ToList()
            };
        }

        /// <summary>
        /// Save a FINAL purchase return (same table).
        /// Forces negative line Qty, validates per-line remaining caps, computes totals from |Qty|.
        /// Overload kept to avoid breaking existing callers (no refunds).
        /// </summary>
        public Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null)
            => SaveReturnAsync(model, lines, user, refunds: null, tillSessionId: null, counterId: null);

        /// <summary>
        /// Save a FINAL purchase return with optional refunds and auto-credit creation.
        /// </summary>
        public async Task<Purchase> SaveReturnAsync(
    Purchase model,
    IEnumerable<PurchaseLine> lines,
    string? user = null,
    IEnumerable<SupplierRefundSpec>? refunds = null,
    int? tillSessionId = null,
    int? counterId = null)
        {
            if (!model.IsReturn)
                throw new InvalidOperationException("Model must be a return (IsReturn=true).");

            // === NEW: Determine mode ===
            var hasRefPurchase = model.RefPurchaseId.HasValue && model.RefPurchaseId > 0;
            var anyLineReferencesOriginal = lines.Any(l => l.RefPurchaseLineId.HasValue);

            // === NEW: Validation for the two modes ===
            if (hasRefPurchase)
            {
                // Referenced return is OK; lines MAY reference original lines.
                // (No extra check needed here.)
            }
            else
            {
                // Free-form return: must NOT reference any original purchase line.
                if (anyLineReferencesOriginal)
                    throw new InvalidOperationException("Free-form returns cannot contain lines with RefPurchaseLineId.");
            }

            // Normalize for returns (keep negative qty, round, etc.)
            var lineList = NormalizeAndComputeReturn(lines);

            // === CHANGED: Cap checks only when we HAVE a RefPurchaseId
            if (hasRefPurchase)
            {
                // Cap check vs remaining allowed on the referenced purchase
                var draft = await BuildReturnDraftAsync(model.RefPurchaseId!.Value);
                var maxMap = draft.Lines
                    .Where(x => x.OriginalLineId.HasValue)
                    .ToDictionary(x => x.OriginalLineId!.Value, x => x.MaxReturnQty);

                foreach (var l in lineList)
                {
                    if (l.RefPurchaseLineId.HasValue &&
                        maxMap.TryGetValue(l.RefPurchaseLineId.Value, out var maxAllowed))
                    {
                        var req = Math.Abs(l.Qty); // l.Qty is negative
                        if (req - maxAllowed > 0.0001m)
                            throw new InvalidOperationException("Return qty exceeds remaining for one or more lines.");
                    }
                }
            }
            else
            {
                // Free-form: no cap checks against a base purchase.
                // We still allow editable UnitCost coming from the UI.
            }

            // Compute header totals from the prepared line list
            ComputeHeaderTotalsForReturn(model, lineList);

            model.Status = PurchaseStatus.Final;
            model.ReceivedAtUtc ??= DateTime.UtcNow;
            model.UpdatedAtUtc = DateTime.UtcNow;
            model.UpdatedBy = user;

            using var tx = await _db.Database.BeginTransactionAsync();

            if (model.Id == 0)
            {
                model.CreatedAtUtc = DateTime.UtcNow;
                model.CreatedBy = user;
                model.DocNo = await EnsureReturnNumberAsync(model, CancellationToken.None);
                model.Revision = 0;
                model.Lines = lineList;
                _db.Purchases.Add(model);
            }
            else
            {
                // Amending an existing return
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id);

                if (!existing.IsReturn)
                    throw new InvalidOperationException("Cannot overwrite a purchase with a return.");

                var wasFinal = existing.Status == PurchaseStatus.Final;

                existing.PartyId = model.PartyId;
                existing.TargetType = model.TargetType;
                existing.OutletId = model.OutletId;
                existing.WarehouseId = model.WarehouseId;
                existing.PurchaseDate = model.PurchaseDate;
                existing.VendorInvoiceNo = model.VendorInvoiceNo;

                existing.RefPurchaseId = model.RefPurchaseId; // may be null for free-form

                existing.DocNo = string.IsNullOrWhiteSpace(model.DocNo)
                    ? await EnsureReturnNumberAsync(existing, CancellationToken.None)
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

                existing.Revision = wasFinal
                    ? (existing.Revision <= 0 ? 1 : existing.Revision + 1)
                    : 0;

                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync();

                foreach (var l in lineList)
                {
                    l.Id = 0; l.PurchaseId = existing.Id; l.Purchase = null;
                }
                existing.Lines = lineList;

                model = existing;
            }

            await _db.SaveChangesAsync();

            // === CHANGED: Only auto-apply against original if one exists.
            decimal appliedToOriginal = 0m;
            if (hasRefPurchase)
            {
                try
                {
                    appliedToOriginal = await AutoApplyReturnToOriginalAsync_ReturnApplied(model, user ?? "system");
                }
                catch
                {
                    // non-fatal; optionally log
                }
            }

            // Compute leftover value of the return
            var leftover = model.GrandTotal - appliedToOriginal;

            // If operator entered refunds, record CASH IN (positive delta)
            var totalRefund = Math.Round((refunds ?? Array.Empty<SupplierRefundSpec>()).Sum(r => Math.Max(0, r.Amount)), 2);
            if (totalRefund > 0)
            {
                // For free-form, appliedToOriginal = 0, so this still guards against over-refund
                if (totalRefund > leftover + 0.0001m)
                    throw new InvalidOperationException("Refund exceeds leftover after applying to the original invoice.");

                var outletForCash = model.OutletId ?? 0; // adjust if you want warehouse-specific handling
                var who = user ?? "system";

                foreach (var r in refunds!)
                    await RecordSupplierRefundAsync(
                        returnId: model.Id,
                        supplierId: model.PartyId,
                        outletId: outletForCash,
                        tillSessionId: tillSessionId,
                        counterId: counterId,
                        refund: r,
                        user: who
                    );

                leftover -= totalRefund;
            }

            // Any remaining leftover becomes Supplier Credit
            if (leftover > 0.0001m)
            {
                var credit = new SupplierCredit
                {
                    SupplierId = model.PartyId,
                    OutletId = model.OutletId, // or null to keep credit global per supplier
                    Amount = Math.Round(leftover, 2),
                    Source = $"Return {(string.IsNullOrWhiteSpace(model.DocNo) ? $"#{model.Id}" : model.DocNo)}"
                };
                _db.SupplierCredits.Add(credit);
                await _db.SaveChangesAsync();
            }

            // TODO: Post stock ledger with negative deltas for all lines:
            // - Location: OutletId or WarehouseId depending on model.TargetType
            // - Delta: l.Qty (already negative)
            // - Valuation: l.UnitCost (free-form uses edited cost; referenced uses locked cost)

            await tx.CommitAsync();
            return model;
        }


        private async Task<string> EnsureReturnNumberAsync(Purchase p, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(p.DocNo)) return p.DocNo!;

            var day = (p.ReceivedAtUtc ?? DateTime.UtcNow).Date;
            var prefix = $"PR-{day:yyyyMMdd}-";

            var countToday = await _db.Purchases
                .AsNoTracking()
                .CountAsync(x => x.IsReturn
                              && x.Status == PurchaseStatus.Final
                              && x.ReceivedAtUtc >= day
                              && x.ReceivedAtUtc < day.AddDays(1), ct);

            return prefix + (countToday + 1).ToString("D3");
        }

        // ---------- Helpers ----------

        private static List<PurchaseLine> NormalizeAndCompute(IEnumerable<PurchaseLine> lines)
        {
            // For purchases (not returns). Negative qtys are coerced to zero.
            var list = lines.ToList();
            foreach (var l in list)
            {
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

        private static List<PurchaseLine> NormalizeAndComputeReturn(IEnumerable<PurchaseLine> lines)
        {
            // For returns. FORCE negative quantity; compute amounts on ABS(qty)
            var list = lines.ToList();
            foreach (var l in list)
            {
                if (l.Qty > 0) l.Qty = -l.Qty;                // make sure it's negative
                l.UnitCost = l.UnitCost < 0 ? 0 : l.UnitCost;
                l.Discount = l.Discount < 0 ? 0 : l.Discount;
                l.TaxRate = l.TaxRate < 0 ? 0 : l.TaxRate;

                var qtyAbs = Math.Abs(l.Qty);
                var baseAmt = qtyAbs * l.UnitCost;
                var taxable = Math.Max(0, baseAmt - l.Discount);
                var tax = Math.Round(taxable * (l.TaxRate / 100m), 2);
                l.LineTotal = Math.Round(taxable + tax, 2);

                // IMPORTANT: line.RefPurchaseLineId should be set by caller when "Return With..."
                // (We don't enforce it here because you might allow free-form returns too.)
            }
            return list;
        }

        private static void ComputeHeaderTotals(Purchase p, IReadOnlyCollection<PurchaseLine> lines)
        {
            // Standard purchase math (qty assumed >= 0)
            p.Subtotal = Math.Round(lines.Sum(x => x.Qty * x.UnitCost), 2);
            p.Discount = Math.Round(lines.Sum(x => x.Discount), 2);
            p.Tax = Math.Round(lines.Sum(x =>
                                 Math.Max(0, x.Qty * x.UnitCost - x.Discount) * (x.TaxRate / 100m)), 2);
            // keep p.OtherCharges as set by caller (UI)
            p.GrandTotal = Math.Round(p.Subtotal - p.Discount + p.Tax + p.OtherCharges, 2);

            try
            {
                p.CashPaid = Math.Min(p.CashPaid, p.GrandTotal);
                p.CreditDue = Math.Max(0, p.GrandTotal - p.CashPaid);
            }
            catch { /* ignore if fields not present */ }
        }

        private static void ComputeHeaderTotalsForReturn(Purchase p, IReadOnlyCollection<PurchaseLine> lines)
        {
            // Return math – amounts based on ABS(qty), but the document is a credit (GrandTotal positive).
            var subtotal = lines.Sum(x => Math.Abs(x.Qty) * x.UnitCost);
            var discount = lines.Sum(x => x.Discount);
            var tax = lines.Sum(x => Math.Round(Math.Max(0, Math.Abs(x.Qty) * x.UnitCost - x.Discount) * (x.TaxRate / 100m), 2));

            p.Subtotal = Math.Round(subtotal, 2);
            p.Discount = Math.Round(discount, 2);
            p.Tax = Math.Round(tax, 2);
            p.GrandTotal = Math.Round(p.Subtotal - p.Discount + p.Tax + p.OtherCharges, 2);

            // For returns, you typically *reduce payable*. Payments/credits handled outside.
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

        // ---------- INTERNAL CREDIT/REFUND HELPERS ----------

        /// <summary>
        /// Non-cash adjustment on original purchase for return value. Returns amount applied.
        /// </summary>
        private async Task<decimal> AutoApplyReturnToOriginalAsync_ReturnApplied(Purchase savedReturn, string user)
        {
            if (!savedReturn.IsReturn || savedReturn.RefPurchaseId is null or <= 0)
                return 0m;

            var original = await _db.Purchases
                .Include(p => p.Payments)
                .FirstAsync(p => p.Id == savedReturn.RefPurchaseId.Value);

            if (original.Status == PurchaseStatus.Voided)
                return 0m; // nothing to apply

            var alreadyPaid = original.Payments.Sum(p => p.Amount);
            var due = Math.Max(0, original.GrandTotal - alreadyPaid);
            if (due <= 0) return 0m;

            var toApply = Math.Min(due, savedReturn.GrandTotal);
            if (toApply <= 0) return 0m;

            await AddPaymentAsync(
                purchaseId: original.Id,
                kind: PurchasePaymentKind.Adjustment,
                method: TenderMethod.Other, // non-cash
                amount: toApply,
                note: $"Auto-applied from Return {(string.IsNullOrWhiteSpace(savedReturn.DocNo) ? $"#{savedReturn.Id}" : savedReturn.DocNo)}",
                outletId: original.OutletId ?? savedReturn.OutletId ?? 0,
                supplierId: original.PartyId,
                tillSessionId: null,
                counterId: null,
                user: user
            );

            return toApply;
        }

        /// <summary>
        /// Record supplier refund cash/bank/card IN tied to a purchase return (positive cash delta).
        /// </summary>
        private async Task RecordSupplierRefundAsync(
            int returnId,
            int supplierId,
            int outletId,
            int? tillSessionId,
            int? counterId,
            SupplierRefundSpec refund,
            string user)
        {
            var amt = Math.Round(refund.Amount, 2);
            if (amt <= 0) throw new InvalidOperationException("Refund amount must be > 0.");

            var cash = new CashLedger
            {
                OutletId = outletId,
                CounterId = counterId,
                TillSessionId = tillSessionId,
                TsUtc = DateTime.UtcNow,
                Delta = +amt, // CASH IN
                RefType = "PurchaseReturnRefund",
                RefId = returnId,
                Note = $"{refund.Method} refund — {refund.Note}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user
            };

            _db.CashLedgers.Add(cash);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Consume available SupplierCredits as non-cash adjustments on a purchase.
        /// Prefers outlet-scoped credits, then global credits; oldest first.
        /// </summary>
        private async Task<decimal> ApplySupplierCreditsAsync(
            int supplierId, int? outletId, Purchase purchase, string user)
        {
            // Determine how much we still need to cover
            var currentPaid = purchase.Payments.Sum(p => p.Amount);
            var need = Math.Max(0, purchase.GrandTotal - currentPaid);
            if (need <= 0) return 0m;

            // Pull credits (oldest first). Prefer outlet-specific first, then global.
            var credits = await _db.SupplierCredits
                .Where(c => c.SupplierId == supplierId && (c.OutletId == outletId || c.OutletId == null) && c.Amount > 0)
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync();

            decimal used = 0m;

            foreach (var c in credits)
            {
                if (need <= 0) break;
                var take = Math.Min(c.Amount, need);
                if (take <= 0) continue;

                // Apply as non-cash adjustment to this purchase
                await AddPaymentAsync(
                    purchaseId: purchase.Id,
                    kind: PurchasePaymentKind.Adjustment,
                    method: TenderMethod.Other,
                    amount: take,
                    note: $"Applied Supplier Credit ({c.Source})",
                    outletId: purchase.OutletId ?? outletId ?? 0,
                    supplierId: supplierId,
                    tillSessionId: null,
                    counterId: null,
                    user: user
                );

                // Reduce credit
                c.Amount = Math.Round(c.Amount - take, 2);
                used += take;
                need -= take;
            }

            // Remove zeroed credits to keep table clean
            var zeroed = credits.Where(c => c.Amount <= 0.0001m).ToList();
            if (zeroed.Count > 0)
                _db.SupplierCredits.RemoveRange(zeroed);

            await _db.SaveChangesAsync();
            return used;
        }
    }

    // ---------- Simple DTOs for Return Draft ----------

    public class PurchaseReturnDraft
    {
        public int PartyId { get; set; }
        public StockTargetType TargetType { get; set; }
        public int? OutletId { get; set; }
        public int? WarehouseId { get; set; }
        public int RefPurchaseId { get; set; }
        public List<PurchaseReturnDraftLine> Lines { get; set; } = new();
    }

    public class PurchaseReturnDraftLine
    {
        public int? OriginalLineId { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public decimal UnitCost { get; set; }
        public decimal MaxReturnQty { get; set; }
        public decimal ReturnQty { get; set; }
    }
}
