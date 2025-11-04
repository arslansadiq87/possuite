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
            // 0) Normalize + compute monetary totals on incoming UI payload
            ValidateDestination(model);

            var lineList = NormalizeAndCompute(lines);   // your helper
            ComputeHeaderTotals(model, lineList);        // sets Subtotal/Discount/Tax/GrandTotal on model

            // common header stamps
            model.Status = PurchaseStatus.Final;
            model.ReceivedAtUtc ??= DateTime.UtcNow;
            model.UpdatedAtUtc = DateTime.UtcNow;
            model.UpdatedBy = user;

            // -----------------------------
            // 1) FIRST-TIME FINALIZATION
            // -----------------------------
            if (model.Id == 0)
            {
                model.CreatedAtUtc = DateTime.UtcNow;
                model.CreatedBy = user;
                model.DocNo = await EnsurePurchaseNumberAsync(model, CancellationToken.None);
                model.Revision = 0;

                model.Lines = lineList;
                _db.Purchases.Add(model);

                await _db.SaveChangesAsync();

                // normal purchase postings (IN to the chosen destination)
                await PostPurchaseStockAsync(model, lineList, user ?? "system");

                // non-fatal convenience
                try
                {
                    await ApplySupplierCreditsAsync(
                        supplierId: model.PartyId,
                        outletId: model.OutletId,
                        purchase: model,
                        user: user ?? "system");
                }
                catch { /* ignore */ }

                return model;
            }

            // -----------------------------
            // 2) AMEND OR FIRST-TIME FINAL?
            // -----------------------------
            var existing = await _db.Purchases
                                    .Include(p => p.Lines)
                                    .FirstAsync(p => p.Id == model.Id);

            var wasFinal = existing.Status == PurchaseStatus.Final;

            // --- capture OLD destination before overwriting header ---
            var oldLocType = existing.TargetType == StockTargetType.Warehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;
            var oldLocId = existing.TargetType == StockTargetType.Warehouse
                ? (existing.WarehouseId ?? 0)
                : (existing.OutletId ?? 0);

            // --- compute NEW destination from the incoming model (what user selected) ---
            var newLocType = model.TargetType == StockTargetType.Warehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;
            var newLocId = model.TargetType == StockTargetType.Warehouse
                ? (model.WarehouseId ?? 0)
                : (model.OutletId ?? 0);

            // Will destination change?
            bool destinationChanged =
                (existing.TargetType != model.TargetType) ||
                (oldLocId != newLocId);

            // Update header (lines are immutable once Final; we won't touch them in Amend)
            existing.PartyId = model.PartyId;
            existing.TargetType = model.TargetType;
            existing.OutletId = model.OutletId;
            existing.WarehouseId = model.WarehouseId;
            existing.PurchaseDate = model.PurchaseDate;
            existing.VendorInvoiceNo = model.VendorInvoiceNo;

            existing.DocNo = string.IsNullOrWhiteSpace(model.DocNo)
                ? (existing.DocNo ?? await EnsurePurchaseNumberAsync(existing, CancellationToken.None))
                : model.DocNo;

            // Money totals reflect the desired state user has on screen
            existing.Subtotal = model.Subtotal;
            existing.Discount = model.Discount;
            existing.Tax = model.Tax;
            existing.OtherCharges = model.OtherCharges;
            existing.GrandTotal = model.GrandTotal;

            existing.Status = PurchaseStatus.Final;
            existing.ReceivedAtUtc = model.ReceivedAtUtc;
            existing.UpdatedAtUtc = model.UpdatedAtUtc;
            existing.UpdatedBy = model.UpdatedBy;

            // Revision: bump on amendments only
            if (wasFinal)
                existing.Revision = (existing.Revision <= 0 ? 1 : existing.Revision + 1);
            else
                existing.Revision = 0;

            // ---------------------------------------------------------
            // 2X) IF ALREADY FINAL AND DESTINATION CHANGED → MOVE STOCK
            //      STRATEGY: retag existing postings (Purchase + Amend)
            //      so all readers immediately reflect the new location.
            // ---------------------------------------------------------
            if (wasFinal && destinationChanged)
            {
                var relevantRefTypes = new[] { "Purchase", "PurchaseAmend" };

                // 1) Load ALL postings for this purchase that currently sit at the OLD destination
                var oldPostings = await _db.StockEntries
                    .Where(se => se.RefId == existing.Id
                                 && se.LocationType == oldLocType
                                 && se.LocationId == oldLocId
                                 && relevantRefTypes.Contains(se.RefType))
                    .ToListAsync();

                // Nothing posted at old destination (e.g., it was already moved previously) → nothing to do
                if (oldPostings.Count == 0)
                    goto AfterMove;

                // 2) Negative guard at OLD destination:
                //    Compute total qty we are about to remove (per item) and ensure old location won't go < 0.
                var movingByItem = oldPostings
                    .GroupBy(se => se.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                    .ToList();

                var itemIds = movingByItem.Select(x => x.ItemId).ToArray();

                var onhandOld = await _db.StockEntries
                    .AsNoTracking()
                    .Where(se => se.LocationType == oldLocType
                                 && se.LocationId == oldLocId
                                 && itemIds.Contains(se.ItemId))
                    .GroupBy(se => se.ItemId)
                    .Select(g => new { ItemId = g.Key, OnHand = g.Sum(x => x.QtyChange) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.OnHand);

                var names = await _db.Items
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Name })
                    .ToDictionaryAsync(x => x.Id, x => x.Name);

                var negHits = new List<string>();
                foreach (var m in movingByItem)
                {
                    var curr = onhandOld.TryGetValue(m.ItemId, out var oh) ? oh : 0m;
                    var after = curr - m.Qty;   // we will remove exactly this qty from OLD
                    if (after < 0m)
                    {
                        var label = names.TryGetValue(m.ItemId, out var nm) ? $"{nm} (#{m.ItemId})" : $"Item #{m.ItemId}";
                        negHits.Add($"{label}: on-hand at old {curr:0.##} → after move {after:0.##}");
                    }
                }
                if (negHits.Count > 0)
                    throw new InvalidOperationException(
                        "Cannot change destination — it would make stock negative at the original location:\n" +
                        string.Join("\n", negHits));

                // 3) Retag the existing rows to NEW destination (update in-place).
                //    This keeps the same RefType ("Purchase"/"PurchaseAmend"), same quantities/costs/audit,
                //    but they now reside at the new location so every stock reader reflects it.
                foreach (var se in oldPostings)
                {
                    se.LocationType = newLocType;
                    se.LocationId = newLocId;
                    se.Note = "Relocated on amend (dest change)";
                    // (Optional) se.Ts = DateTime.UtcNow;  // only if you want to bump the timestamp
                }
                await _db.SaveChangesAsync();

            AfterMove:;
            }


            // -----------------------------------------------
            // 2A) FIRST-TIME FINAL FROM DRAFT (NOT FINAL YET)
            // -----------------------------------------------
            if (!wasFinal)
            {
                // replace lines once (draft → final)
                _db.PurchaseLines.RemoveRange(existing.Lines);
                await _db.SaveChangesAsync();

                foreach (var l in lineList)
                {
                    l.Id = 0;
                    l.PurchaseId = existing.Id;
                    l.Purchase = null;
                }
                existing.Lines = lineList;

                await _db.SaveChangesAsync();

                // normal purchase postings (IN to current destination on header)
                await PostPurchaseStockAsync(existing, lineList, user ?? "system");

                try
                {
                    await ApplySupplierCreditsAsync(
                        supplierId: existing.PartyId,
                        outletId: existing.OutletId,
                        purchase: existing,
                        user: user ?? "system");
                }
                catch { /* ignore */ }

                return existing;
            }

            // -----------------------------------------------
            // 2B) AMENDMENT OF AN ALREADY-FINAL PURCHASE
            // -----------------------------------------------
            // IMPORTANT: Do NOT modify existing.Lines.
            // Baseline must be: ORIGINAL LINES + ALL PRIOR AMENDMENTS (PurchaseAmend).
            // Then write ONLY the new delta as StockEntry (append-only).

            // Sum prior amendment deltas for this purchase (all locations)
            var priorAmendQtyByItem = await _db.StockEntries
                .AsNoTracking()
                .Where(se => se.RefType == "PurchaseAmend" && se.RefId == existing.Id)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

            // Build BASELINE map (current effective qty = original + prior amendments)
            var cur = existing.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        qty = g.Sum(x => x.Qty) + (priorAmendQtyByItem.TryGetValue(g.Key, out var aq) ? aq : 0m),
                        unitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m
                    });

            // Include items that exist only via prior amendments (no original line)
            foreach (var kv in priorAmendQtyByItem)
            {
                if (!cur.ContainsKey(kv.Key))
                {
                    cur[kv.Key] = new { qty = kv.Value, unitCost = 0m };
                }
            }

            // DESIRED state from the screen (incoming now)
            var nxt = lineList
                .GroupBy(l => l.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        qty = g.Sum(x => x.Qty),
                        unitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m
                    });

            var allItemIds = cur.Keys.Union(nxt.Keys).ToList();

            // === HARD RULE: Amended qty per item cannot be below the qty already returned for THIS purchase
            var returnedByItem = await _db.PurchaseLines
                .AsNoTracking()
                .Where(l => l.Purchase.IsReturn
                         && l.Purchase.Status == PurchaseStatus.Final   // <— Final only
                         && l.Purchase.RefPurchaseId == existing.Id)
                .GroupBy(l => l.ItemId)
                .Select(g => new { ItemId = g.Key, Returned = -g.Sum(x => x.Qty) }) // flip sign → positive
                .ToDictionaryAsync(x => x.ItemId, x => x.Returned);

            if (returnedByItem.Count > 0)
            {
                foreach (var kv in returnedByItem)
                {
                    var itemId = kv.Key;
                    var returned = kv.Value; // positive
                    var desired = nxt.TryGetValue(itemId, out var n) ? n.qty : 0m;

                    if (desired < returned)
                    {
                        var name = await _db.Items.Where(i => i.Id == itemId)
                                                  .Select(i => i.Name)
                                                  .FirstOrDefaultAsync() ?? $"Item #{itemId}";
                        throw new InvalidOperationException(
                            $"Cannot amend '{name}' below {returned:0.##} because that much is already returned. " +
                            $"Amended qty: {desired:0.##}.");
                    }
                }
            }

            // Resolve normalized NEW location from header (it may have changed above)
            InventoryLocationType locType;
            int locId;
            if (existing.TargetType == StockTargetType.Warehouse)
            {
                locType = InventoryLocationType.Warehouse;
                if (!existing.WarehouseId.HasValue)
                    throw new InvalidOperationException("TargetType=Warehouse but WarehouseId is null.");
                locId = existing.WarehouseId.Value;
            }
            else
            {
                locType = InventoryLocationType.Outlet;
                if (!existing.OutletId.HasValue)
                    throw new InvalidOperationException("TargetType=Outlet but OutletId is null.");
                locId = existing.OutletId.Value;
            }

            // For StockEntry.OutletId (required): use purchase's OutletId when present, else 0 for warehouse-only receipts
            var entryOutletId2 = existing.OutletId ?? 0;

            // ---- Make 2B atomic
            using (var tx = await _db.Database.BeginTransactionAsync())
            {
                // 1) Compute deltas first (DON'T add to Db yet)
                var deltas = new List<(int itemId, decimal dQty, decimal unitCost)>();
                var deltaByItem = new Dictionary<int, decimal>();

                foreach (var itemId in allItemIds)
                {
                    var before = cur.TryGetValue(itemId, out var c) ? c.qty : 0m;
                    var after = nxt.TryGetValue(itemId, out var n) ? n.qty : 0m;
                    var dQty = after - before; // + IN, – OUT
                    if (dQty == 0m) continue;

                    if (deltaByItem.TryGetValue(itemId, out var agg))
                        deltaByItem[itemId] = agg + dQty;
                    else
                        deltaByItem[itemId] = dQty;

                    var unitCost =
                        (nxt.TryGetValue(itemId, out var nMeta) ? nMeta.unitCost
                         : (cur.TryGetValue(itemId, out var cMeta) ? cMeta.unitCost : 0m));

                    deltas.Add((itemId, dQty, unitCost));
                }

                // 2) Guard: ensure these deltas won't make on-hand negative at destination
                if (deltaByItem.Count > 0)
                {
                    var itemIdsForGuard = deltaByItem.Keys.ToList();

                    var onhand = await _db.StockEntries
                        .AsNoTracking()
                        .Where(se => se.LocationType == locType
                                  && se.LocationId == locId
                                  && itemIdsForGuard.Contains(se.ItemId))
                        .GroupBy(se => se.ItemId)
                        .Select(g => new { ItemId = g.Key, OnHand = g.Sum(x => x.QtyChange) })
                        .ToDictionaryAsync(x => x.ItemId, x => x.OnHand);

                    var names = await _db.Items
                        .Where(i => itemIdsForGuard.Contains(i.Id))
                        .Select(i => new { i.Id, i.Name })
                        .ToDictionaryAsync(x => x.Id, x => x.Name);

                    var negHits = new List<string>();
                    foreach (var kv in deltaByItem)
                    {
                        var itemId = kv.Key;
                        var d = kv.Value;
                        var curOn = onhand.TryGetValue(itemId, out var oh) ? oh : 0m;
                        var nextOn = curOn + d;

                        if (nextOn < 0m)
                        {
                            var label = names.TryGetValue(itemId, out var nm) ? $"{nm} (#{itemId})" : $"Item #{itemId}";
                            negHits.Add($"{label}: on-hand {curOn:0.##} + change {d:0.##} = {nextOn:0.##}");
                        }
                    }

                    if (negHits.Count > 0)
                        throw new InvalidOperationException(
                            "This amendment would make stock negative at the destination:\n" +
                            string.Join("\n", negHits) +
                            "\nReceive stock or undo other movements first.");
                }

                // 3) Safe → append StockEntry rows now
                foreach (var (itemId, dQty, unitCost) in deltas)
                {
                    _db.StockEntries.Add(new StockEntry
                    {
                        OutletId = entryOutletId2,
                        ItemId = itemId,
                        QtyChange = dQty,
                        UnitCost = unitCost,
                        LocationType = locType,
                        LocationId = locId,
                        RefType = "PurchaseAmend",
                        RefId = existing.Id,
                        Ts = DateTime.UtcNow,
                        Note = $"Amend Rev {existing.Revision}"
                    });
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            return existing;

        }

        // Resolve or create per-outlet "Supplier Advances" posting account without using WPF services.
        private async Task<int> GetSupplierAdvancesAccountIdAsync(int outletId)
        {
            // Need outlet code to build our deterministic account code
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"113-{outlet.Code}-ADV";

            // If exists, return it
            var existingId = await _db.Accounts
                .AsNoTracking()
                .Where(a => a.OutletId == outletId && a.Code == code)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();
            if (existingId != 0) return existingId;

            // Try to find an "Assets" header to attach under; if not found, parent is null
            int? assetHeaderId = await _db.Accounts
                .AsNoTracking()
                .Where(a => a.OutletId == outletId && a.IsHeader &&
                            (a.Code == "1" || a.Name == "Assets"))     // adjust if your chart differs
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync();

            var acc = new Pos.Domain.Entities.Account
            {
                OutletId = outletId,
                Code = code,
                Name = "Supplier Advances",
                Type = Pos.Domain.Entities.AccountType.Asset,
                IsHeader = false,
                AllowPosting = true,
                ParentId = assetHeaderId,
                IsSystem = true
            };

            _db.Accounts.Add(acc);
            await _db.SaveChangesAsync();
            return acc.Id;
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
    string user,
    int? bankAccountId = null) // NEW
        {
            // ---------- Load & basic guards ----------
            var purchase = await _db.Purchases
                .Include(p => p.Payments)
                .FirstAsync(p => p.Id == purchaseId);

            if (purchase.Status == PurchaseStatus.Voided)
                throw new InvalidOperationException("Cannot pay a voided purchase.");

            var amt = Math.Round(amount, 2);
            if (amt <= 0m)
                throw new InvalidOperationException("Amount must be > 0.");

            // ---------- Business rules by status ----------
            switch (purchase.Status)
            {
                case PurchaseStatus.Draft:
                    // Only ADVANCE allowed on held (draft) purchases
                    if (kind != PurchasePaymentKind.Advance)
                        throw new InvalidOperationException("Only Advance payments are allowed on held (draft) purchases.");
                    break;

                case PurchaseStatus.Final:
                    // For finalized purchases, use OnReceive or Adjustment
                    if (kind == PurchasePaymentKind.Advance)
                        throw new InvalidOperationException("Use OnReceive or Adjustment for finalized purchases.");
                    break;

                default:
                    break;
            }

            // Prevent overpayment (against GrandTotal)
            var currentPaid = purchase.Payments.Sum(p => p.Amount);
            if (currentPaid + amt > purchase.GrandTotal)
                throw new InvalidOperationException("Payment exceeds total.");

            // Determine outlet on which to record the payment
            var payOutletId =
                (purchase.TargetType == StockTargetType.Outlet && purchase.OutletId is int po && po > 0)
                ? po
                : (outletId > 0 ? outletId : throw new InvalidOperationException("Outlet is required for recording the payment."));

            // ---------- Method constraints ----------
            if (method != TenderMethod.Cash && method != TenderMethod.Bank)
                throw new NotSupportedException("Only Cash and Bank methods are allowed for purchase payments.");

            if (method == TenderMethod.Bank)
            {
                // Require PurchaseBankAccountId configured for the outlet
                var settings = await _db.InvoiceSettings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.OutletId == payOutletId);

                if (settings?.PurchaseBankAccountId is null)
                    throw new InvalidOperationException("Bank payments are disabled: configure Purchase Bank Account in Invoice Settings for this outlet.");

                if (bankAccountId is null)
                    throw new InvalidOperationException("Select a bank account for this payment.");
            }

            // ---------- Atomic write: payment + GL + (cash ledger) ----------
            using var tx = await _db.Database.BeginTransactionAsync();

            var pay = new PurchasePayment
            {
                PurchaseId = purchase.Id,
                SupplierId = purchase.PartyId, // trust the purchase header
                OutletId = payOutletId,
                TsUtc = DateTime.UtcNow,
                Kind = kind,
                Method = method,
                Amount = amt,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user,
                BankAccountId = bankAccountId // null for Cash; required for Bank (validated above)
            };

            _db.PurchasePayments.Add(pay);
            await _db.SaveChangesAsync();

            // ---------- GL Posting ----------
            // Resolve credit account for payment: Cash (per-outlet) OR chosen Bank account
            var ts = DateTime.UtcNow;

            int creditAccountId;
            if (method == TenderMethod.Cash)
            {
                // Cash-in-Hand account per outlet: code "111-{Outlet.Code}"
                var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == payOutletId);
                var cashCode = $"11101-{outlet.Code}";
                creditAccountId = await _db.Accounts.AsNoTracking()
                    .Where(a => a.Code == cashCode && a.OutletId == payOutletId)
                    .Select(a => a.Id)
                    .FirstAsync();
            }
            else // Bank
            {
                creditAccountId = pay.BankAccountId!.Value; // enforced above
            }

            if (purchase.Status == PurchaseStatus.Draft)
            {
                // DRAFT: Dr Supplier Advances, Cr Cash/Bank
                //var coa = App.Services.GetRequiredService<ICoaService>();
                var advancesId = await ResolveSupplierAdvancesAccountIdAsync(payOutletId);


                _db.GlEntries.Add(new Pos.Domain.Accounting.GlEntry
                {
                    TsUtc = ts,
                    OutletId = payOutletId,
                    AccountId = advancesId,
                    Debit = amt,
                    Credit = 0m,
                    DocType = Pos.Domain.Accounting.GlDocType.Purchase,
                    DocId = purchase.Id,
                    Memo = $"Advance to supplier ({method})"
                });
                _db.GlEntries.Add(new Pos.Domain.Accounting.GlEntry
                {
                    TsUtc = ts,
                    OutletId = payOutletId,
                    AccountId = creditAccountId,
                    Debit = 0m,
                    Credit = amt,
                    DocType = Pos.Domain.Accounting.GlDocType.Purchase,
                    DocId = purchase.Id,
                    Memo = $"Advance to supplier ({method})"
                });

                await _db.SaveChangesAsync();
            }
            else if (purchase.Status == PurchaseStatus.Final)
            {
                // FINAL: Dr Accounts Payable (AP), Cr Cash/Bank
                // NOTE: adjust "6100" if your AP code is different.
                var apId = await _db.Accounts.AsNoTracking()
                    .Where(a => a.Code == "6100")
                    .Select(a => a.Id)
                    .FirstAsync();

                _db.GlEntries.Add(new Pos.Domain.Accounting.GlEntry
                {
                    TsUtc = ts,
                    OutletId = payOutletId,
                    AccountId = apId,
                    Debit = amt,
                    Credit = 0m,
                    DocType = Pos.Domain.Accounting.GlDocType.Purchase,
                    DocId = purchase.Id,
                    Memo = $"Supplier payment ({kind}, {method})"
                });
                _db.GlEntries.Add(new Pos.Domain.Accounting.GlEntry
                {
                    TsUtc = ts,
                    OutletId = payOutletId,
                    AccountId = creditAccountId,
                    Debit = 0m,
                    Credit = amt,
                    DocType = Pos.Domain.Accounting.GlDocType.Purchase,
                    DocId = purchase.Id,
                    Memo = $"Supplier payment ({kind}, {method})"
                });

                await _db.SaveChangesAsync();
            }

            // ---------- Cash drawer (only for Cash) ----------
            if (method == TenderMethod.Cash)
            {
                _db.CashLedgers.Add(new CashLedger
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
                });
                await _db.SaveChangesAsync();
            }

            // ---------- Snapshots (paid/due) ----------
            // Recompute from DB to include this brand-new row reliably
            var newPaid = await _db.PurchasePayments
                .Where(p => p.PurchaseId == purchase.Id)
                .SumAsync(p => p.Amount);

            purchase.CashPaid = newPaid;
            purchase.CreditDue = Math.Max(0, purchase.GrandTotal - newPaid);
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return pay;
        }

        // ----------------- Account resolvers (no App / no ICoaService) -----------------

        /// <summary>
        /// Returns the Outlet Cash-in-Hand account id using code pattern "111-{Outlet.Code}".
        /// Throws a clear error if not found (so you notice missing seeding).
        /// </summary>
        private async Task<int> ResolveCashAccountIdAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var cashCode = $"11101-{outlet.Code}";
            var cashId = await _db.Accounts.AsNoTracking()
                .Where(a => a.Code == cashCode && a.OutletId == outletId)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();

            if (cashId == 0)
                throw new InvalidOperationException($"Cash account '{cashCode}' not found for outlet #{outletId}. Make sure COA seeding created it.");

            return cashId;
        }

        /// <summary>
        /// Returns (or creates) the per-outlet "Supplier Advances" posting account under Assets.
        /// Code pattern: "113-{Outlet.Code}-ADV".
        /// If the asset header cannot be found, throws with a clear message.
        /// </summary>
        private async Task<int> ResolveSupplierAdvancesAccountIdAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"113-{outlet.Code}-ADV";

            // Try existing
            var existingId = await _db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && a.OutletId == outletId)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();
            if (existingId != 0) return existingId;

            // Find an Assets header for this outlet (preferred) or a shared one.
            var assetHeader = await _db.Accounts
                .Where(a => a.IsHeader && a.AllowPosting == false && a.Type == Pos.Domain.Entities.AccountType.Asset
                            && (a.OutletId == outletId || a.OutletId == null))
                .OrderByDescending(a => a.OutletId) // prefer outlet-specific over shared
                .FirstOrDefaultAsync();

            if (assetHeader == null)
                throw new InvalidOperationException("Assets header account not found. Ensure your Chart of Accounts seeding includes an Asset header.");

            var acc = new Pos.Domain.Entities.Account
            {
                OutletId = outletId,
                Code = code,
                Name = "Supplier Advances",
                Type = Pos.Domain.Entities.AccountType.Asset,
                IsHeader = false,
                AllowPosting = true,
                ParentId = assetHeader.Id,
                IsSystem = true
            };

            _db.Accounts.Add(acc);
            await _db.SaveChangesAsync();
            return acc.Id;
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
        public async Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, IEnumerable<SupplierRefundSpec>? refunds = null, int? tillSessionId = null, int? counterId = null)
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
                ValidateDestination(model);
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
            await PostPurchaseReturnStockAsync(model, lineList, user ?? "system");

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

        public async Task<decimal> GetOnHandAsync(int itemId, StockTargetType target, int? outletId, int? warehouseId)
        {
            if (itemId <= 0) return 0m;

            if (target == StockTargetType.Outlet)
            {
                if (outletId is not int o || o <= 0) return 0m;

                return await _db.StockEntries
                    .Where(se => se.ItemId == itemId
                              && se.LocationType == InventoryLocationType.Outlet
                              && se.LocationId == o)
                    .SumAsync(se => se.QtyChange);
            }
            else // Warehouse
            {
                if (warehouseId is not int w || w <= 0) return 0m;

                return await _db.StockEntries
                    .Where(se => se.ItemId == itemId
                              && se.LocationType == InventoryLocationType.Warehouse
                              && se.LocationId == w)
                    .SumAsync(se => se.QtyChange);
            }
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

        // using Microsoft.EntityFrameworkCore;
        // using Pos.Domain.Entities;
        // ...

        private async Task PostPurchaseStockAsync(Purchase model, IEnumerable<PurchaseLine> lines, string user)
        {
            // Determine ledger location
            var locType = model.TargetType == StockTargetType.Warehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;

            var locId = model.TargetType == StockTargetType.Warehouse
                ? model.WarehouseId!.Value
                : model.OutletId!.Value;

            // If this purchase was already FINAL and is being amended, remove previous postings
            var prior = await _db.StockEntries
                .Where(se => se.RefType == "Purchase" && se.RefId == model.Id)
                .ToListAsync();
            if (prior.Count > 0)
            {
                _db.StockEntries.RemoveRange(prior);
                await _db.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;

            foreach (var l in lines)
            {
                _db.StockEntries.Add(new StockEntry
                {
                    Ts = now,
                    ItemId = l.ItemId,
                    LocationType = locType,
                    LocationId = locId,
                    QtyChange = l.Qty,        // Purchase FINAL = IN
                    UnitCost = l.UnitCost,   // keep for costing/audit
                    RefType = "Purchase",
                    RefId = model.Id,
                    Note = model.VendorInvoiceNo
                });
            }

            await _db.SaveChangesAsync();
        }

        private async Task PostPurchaseReturnStockAsync(Purchase model, IEnumerable<PurchaseLine> lines, string user)
        {
            // ---- Resolve where to post OUT (debit) ----
            InventoryLocationType locType;
            int locId;

            if (model.RefPurchaseId is int rid && rid > 0)
            {
                // WITH-INVOICE: reduce where the original purchase was received
                var orig = await _db.Purchases.AsNoTracking().FirstAsync(p => p.Id == rid);

                if (orig.TargetType == StockTargetType.Warehouse)
                {
                    locType = InventoryLocationType.Warehouse;
                    locId = orig.WarehouseId!.Value;
                }
                else
                {
                    locType = InventoryLocationType.Outlet;
                    locId = orig.OutletId!.Value;
                }
            }
            else
            {
                // WITHOUT-INVOICE: reduce at the user-selected header source
                if (model.TargetType == StockTargetType.Warehouse)
                {
                    locType = InventoryLocationType.Warehouse;
                    locId = model.WarehouseId!.Value;
                }
                else
                {
                    locType = InventoryLocationType.Outlet;
                    locId = model.OutletId!.Value;
                }
            }

            // --- keep the rest of your method as-is (remove prior, add rows, SaveChanges) ---
            var prior = await _db.StockEntries
                .Where(se => se.RefType == "PurchaseReturn" && se.RefId == model.Id)
                .ToListAsync();
            if (prior.Count > 0)
            {
                _db.StockEntries.RemoveRange(prior);
                await _db.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;
            foreach (var l in lines)
            {
                _db.StockEntries.Add(new StockEntry
                {
                    Ts = now,
                    ItemId = l.ItemId,
                    LocationType = locType,
                    LocationId = locId,
                    QtyChange = l.Qty,      // negative → OUT
                    UnitCost = l.UnitCost,
                    RefType = "PurchaseReturn",
                    RefId = model.Id,
                    Note = model.VendorInvoiceNo
                });
            }
            await _db.SaveChangesAsync();
        }


        public async Task VoidPurchaseAsync(int purchaseId, string reason, string? user = null)
        {
            // Load the purchase (non-return)
            var p = await _db.Purchases
                .AsNoTracking()
                .FirstAsync(x => x.Id == purchaseId && !x.IsReturn);

            if (p.Status == PurchaseStatus.Voided) return;

            // Resolve the exact ledger location used by postings
            var locType = p.TargetType == StockTargetType.Warehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;
            var locId = p.TargetType == StockTargetType.Warehouse
                ? p.WarehouseId!.Value
                : p.OutletId!.Value;

            // All postings tied to this purchase that must be removed
            // IMPORTANT: include both "Purchase" and "PurchaseAmend"
            var postings = await _db.StockEntries
                .Where(se => se.RefId == p.Id &&
                      (se.RefType == "Purchase" || se.RefType == "PurchaseAmend" || se.RefType == "PurchaseMove"))
                .ToListAsync();

            // Nothing posted? Just mark void and exit.
            if (postings.Count == 0)
            {
                var entity = await _db.Purchases.FirstAsync(x => x.Id == p.Id);
                entity.Status = PurchaseStatus.Voided;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                entity.UpdatedBy = user;
                await _db.SaveChangesAsync();
                return;
            }

            // Compute the net qty that will be REMOVED by voiding
            // (removing postings changes on-hand by: Δ = - Σ(post.QtyChange))
            var netByItem = postings
                .GroupBy(se => se.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(se => se.QtyChange)    // sum of (+purchase +amend deltas)
                );

            // Check current on-hand at the same location for these items
            var itemIds = netByItem.Keys.ToArray();

            var onhandByItem = await _db.StockEntries
                .AsNoTracking()
                .Where(se => se.LocationType == locType &&
                             se.LocationId == locId &&
                             itemIds.Contains(se.ItemId))
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, OnHand = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.OnHand);

            // Validate no item would go below zero after removal
            var violations = new List<string>();
            foreach (var kv in netByItem)
            {
                var itemId = kv.Key;
                var sumPosted = kv.Value;               // can be + or − overall
                var cur = onhandByItem.TryGetValue(itemId, out var oh) ? oh : 0m;
                var after = cur - sumPosted;            // because removal = -sumPosted

                if (after < 0m)
                    violations.Add($"Item {itemId}: on-hand {cur:0.##} → after void {after:0.##}");
            }

            if (violations.Count > 0)
            {
                var msg = "Cannot void purchase — it would make stock negative:\n" +
                          string.Join("\n", violations);
                throw new InvalidOperationException(msg);
            }

            // Safe to void: remove all purchase-linked postings atomically and mark void
            using var tx = await _db.Database.BeginTransactionAsync();

            _db.StockEntries.RemoveRange(postings);

            var entity2 = await _db.Purchases.FirstAsync(x => x.Id == p.Id);
            entity2.Status = PurchaseStatus.Voided;
            entity2.UpdatedAtUtc = DateTime.UtcNow;
            entity2.UpdatedBy = user;
            // (Optional) record audit row if you keep one
            //_db.AuditLogs.Add(new AuditLog { ... });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }


        public async Task VoidReturnAsync(int returnId, string reason, string? user = null)
        {
            var r = await _db.Purchases.Include(x => x.Lines)
                                       .FirstAsync(x => x.Id == returnId && x.IsReturn);
            if (r.Status == PurchaseStatus.Voided) return;
            var postings = await _db.StockEntries
                .Where(se => se.RefType == "PurchaseReturn" && se.RefId == r.Id)
                .ToListAsync();
            if (postings.Count > 0) _db.StockEntries.RemoveRange(postings);

            r.Status = PurchaseStatus.Voided;
            r.UpdatedAtUtc = DateTime.UtcNow;
            r.UpdatedBy = user;
            await _db.SaveChangesAsync();
        }

        public sealed class PurchaseLineEffective
        {
            public int ItemId { get; set; }
            public string? Sku { get; set; }
            public string? Name { get; set; }
            public decimal Qty { get; set; }
            public decimal UnitCost { get; set; }  // display-only; keep simple (avg of base lines)
            public decimal Discount { get; set; }  // display-only (sum of base discounts)
            public decimal TaxRate { get; set; }   // display-only (avg of base tax rates)
        }

        public async Task<List<PurchaseLineEffective>> GetEffectiveLinesAsync(int purchaseId)
        {
            // original lines (grouped by Item)
            var baseLines = await _db.PurchaseLines
                .AsNoTracking()
                .Where(l => l.PurchaseId == purchaseId)
                .GroupBy(l => l.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Qty = g.Sum(x => x.Qty),
                    UnitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m,
                    Discount = Math.Round(g.Sum(x => x.Discount), 2),
                    TaxRate = g.Any() ? Math.Round(g.Average(x => x.TaxRate), 2) : 0m,
                })
                .ToDictionaryAsync(x => x.ItemId, x => x);
            // prior amendment deltas (qty only)
            var amendQty = await _db.StockEntries
                .AsNoTracking()
                .Where(se => se.RefType == "PurchaseAmend" && se.RefId == purchaseId)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Qty);
            // build effective map
            var ids = baseLines.Keys.Union(amendQty.Keys).ToList();
            var effective = new List<PurchaseLineEffective>(ids.Count);
            // minimal item meta
            var itemsMeta = await _db.Items
                .AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, i.Name, i.Price, i.DefaultTaxRatePct })
                .ToDictionaryAsync(x => x.Id, x => x);

            foreach (var id in ids)
            {
                baseLines.TryGetValue(id, out var b);
                amendQty.TryGetValue(id, out var aQty);
                var qty = (b?.Qty ?? 0m) + (aQty);
                if (qty <= 0) continue; // nothing left to show
                var meta = itemsMeta.TryGetValue(id, out var m) ? m : null;
                effective.Add(new PurchaseLineEffective
                {
                    ItemId = id,
                    Sku = meta?.Sku ?? "",
                    Name = meta?.Name ?? $"Item #{id}",
                    Qty = qty,
                    UnitCost = b?.UnitCost ?? (meta?.Price ?? 0m),      // display only
                    Discount = b?.Discount ?? 0m,
                    TaxRate = b?.TaxRate ?? (meta?.DefaultTaxRatePct ?? 0m)
                });
            }
            return effective
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<decimal> GetRemainingReturnableQtyAsync(int purchaseLineId)
        {
            // original line qty (may be negative)
            var orig = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.Id == purchaseLineId)
                .Select(x => (decimal?)x.Qty)
                .FirstOrDefaultAsync();

            if (orig is null) return 0m;
            // SUM of positive magnitudes for all returns that reference this line
            // Use ternary to emulate ABS() and nullable SUM to handle empty set => 0
            var returned = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.RefPurchaseLineId == purchaseLineId)
                .Select(x => (decimal?)(x.Qty < 0 ? -x.Qty : x.Qty))
                .SumAsync() ?? 0m;

            var origAbs = orig.Value < 0 ? -orig.Value : orig.Value;
            var remaining = Math.Max(0m, origAbs - returned);
            return remaining;
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
