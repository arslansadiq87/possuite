// Pos.Persistence/Services/PurchasesService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Domain.Models.Purchases;   // <-- add
using Pos.Persistence.Sync;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pos.Persistence.Services 
{
    public class PurchasesService : IPurchasesService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox; // NEW
        private readonly IInventoryReadService _inv;   // <-- add
        private readonly IGlPostingServiceDb _gl; // ✅ add
        //private readonly IGlReadService _glRead; // ✅ add   
        private readonly ICoaService _coa; // ✅ add this

        public PurchasesService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox, IInventoryReadService inv, IGlPostingServiceDb gl, ICoaService coa)
        {
            _dbf = dbf;
            _outbox = outbox;
            _inv = inv; // <-- add
            _gl = gl; // ✅ add
            _coa = coa;
            //_glRead = glRead;
        }

        

        public async Task<Purchase> SaveDraftAsync(Purchase draft, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
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

                _db.Purchases.Add(draft);
                await _db.SaveChangesAsync(ct);

                foreach (var l in lineList)
                {
                    l.Id = 0;
                    l.PurchaseId = draft.Id;
                    l.Purchase = null;
                }
                await _db.PurchaseLines.AddRangeAsync(lineList, ct);
            }
            else
            {
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == draft.Id, ct);
                // header fields allowed in Draft
                existing.PartyId = draft.PartyId;
                existing.LocationType = draft.LocationType;
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
                //await _db.SaveChangesAsync();
                await _db.SaveChangesAsync(ct);

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
            await _db.SaveChangesAsync(ct);
            return draft;
        }

        private async Task<string> EnsurePurchaseNumberAsync(PosClientDbContext db, Purchase p, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(p.DocNo)) return p.DocNo!;
            var today = DateTime.UtcNow.Date;
            var prefix = $"PO-{today:yyyyMMdd}-";
            var countToday = await db.Purchases
                .AsNoTracking()
                .CountAsync(x => x.Status == PurchaseStatus.Final
                              && x.ReceivedAtUtc >= today
                              && x.ReceivedAtUtc < today.AddDays(1), ct);
            return prefix + (countToday + 1).ToString("D3");
        }


        // WRAPPER: for callers that don’t have a db
        public async Task<Purchase> ReceiveAsync(
            Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await ReceiveAsync(db, model, lines, user, ct);
        }



        public async Task<Purchase> ReceiveAsync(
    PosClientDbContext db,
    Purchase model,
    IEnumerable<PurchaseLine> lines,
    string? user = null,
    CancellationToken ct = default)
        {
            // diagnostics
            var stepId = Guid.NewGuid().ToString("N");
            string P(string p) => $"[ReceiveAsync:{stepId}] {p}";
            void Log(string msg) => global::System.Diagnostics.Debug.WriteLine($"{DateTime.UtcNow:O} {msg}");

            Log(P("ENTER"));
            Log(P($"ARGS model.Id={model?.Id} user='{user}' lines.Count={lines?.Count() ?? 0}"));

            // 0) Validate + normalize + compute header
            var normalized = ValidateAndNormalizeAsync(model, lines, user, Log);

            // 1) Transaction (ambient-aware)
            var ownsTx = db.Database.CurrentTransaction is null;
            var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : db.Database.CurrentTransaction;
            Log(P($"TX BEGIN ownsTx={ownsTx}"));

            try
            {
                Purchase persisted;
                if (model.Id == 0)
                {
                    // A) FIRST-TIME FINAL
                    persisted = await PersistFirstFinalAsync(db, model, normalized, user, Log, ct);

                    // Stock
                    await PostPurchaseStockAsync(db, persisted, normalized, user ?? "system", ct);

                    // Credits (best-effort)
                    await ApplySupplierCreditsSafelyAsync(db, persisted, user, Log, ct);

                    // GL (gross)
                    await PostGrossAsync(db, persisted, Log, ct);
                }
                else
                {
                    // Load current to decide branch
                    var existing = await db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id, ct);
                    var wasFinal = existing.Status == PurchaseStatus.Final;

                    if (!wasFinal)
                    {
                        // B) DRAFT → FINAL
                        persisted = await PersistDraftToFinalAsync(db, existing, normalized, user, Log, ct);

                        // Stock
                        await PostPurchaseStockAsync(db, persisted, normalized, user ?? "system", ct);

                        // GL (gross)
                        await PostGrossAsync(db, persisted, Log, ct);

                        // Credits (best-effort)
                        await ApplySupplierCreditsSafelyAsync(db, persisted, user, Log, ct);
                    }
                    else
                    {
                        // C) AMEND FINAL
                        // Capture OLD values BEFORE we modify/save anything
                        var prevGrand = existing.GrandTotal;
                        var (oldLocType, oldLocId) = ResolveExistingDestination(existing);

                        // Update header with incoming (does NOT SaveChanges itself)
                        UpdateHeaderForAmend(existing, model, normalized, user, Log);
                        var (newLocType, newLocId) = ResolveExistingDestination(existing);
                        var destChanged = oldLocType != newLocType || oldLocId != newLocId;

                        // If destination changed, relocate prior stock postings safely (this saves)
                        if (destChanged)
                            await ApplyDestinationMoveIfNeededAsync(db, existing, oldLocType, oldLocId, newLocType, newLocId, Log, ct);

                        // Compute deltas (qty by item) and negative guard; then post stock deltas (this saves)
                        var deltas = await BuildAmendmentDeltasAsync(db, existing, normalized, Log, ct);
                        await GuardNegativeOnHandForDeltasAsync(_inv, deltas, newLocType, newLocId, Log, ct);
                        await PostAmendmentStockAsync(db, existing, deltas, Log, ct);

                        // Now compute GL delta *in memory* using prevGrand we captured earlier
                        var deltaGrand = existing.GrandTotal - prevGrand;
                        Log($"AMEND deltaGrand={deltaGrand:0.####} (prev={prevGrand:0.####} → new={existing.GrandTotal:0.####})");

                        // GL (revision delta) — even if deltaGrand == 0, we simply skip inside poster
                        await PostRevisionDeltaAsync(db, existing, deltaGrand, Log, ct);

                        persisted = existing;
                    }

                }

                // Outbox entry for header (after mutations)
                await _outbox.EnqueueUpsertAsync(db, persisted, ct);
                await db.SaveChangesAsync(ct);

                if (ownsTx && tx is not null) await tx.CommitAsync(ct);
                Log(P("TX COMMIT"));
                Log(P("EXIT"));
                return persisted;
            }
            catch (Exception ex)
            {
                if (ownsTx && tx is not null)
                {
                    try { await tx.RollbackAsync(ct); Log(P("TX ROLLBACK")); } catch { }
                }
                Log(P($"ERROR {ex.GetType().Name}: {ex.Message}"));
                throw new InvalidOperationException($"ReceiveAsync failed ({ex.GetType().Name}): {ex.Message}", ex);
            }
        }

        #region Receive Pipeline (small helpers)

        private List<PurchaseLine> ValidateAndNormalizeAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user, Action<string> Log)
        {
            Log("00.Validate+Normalize.START");

            // Destination & header cash guards
            ValidateDestination(model);
            if (model.CashPaid > 0m || model.CreditDue > 0m)
                Log($"WARN incoming header cash fields will be zeroed: CashPaid={model.CashPaid}, CreditDue={model.CreditDue}");
            model.CashPaid = 0m; model.CreditDue = 0m;

            // Normalize lines & header totals
            var lineList = NormalizeAndCompute(lines);
            ComputeHeaderTotals(model, lineList);

            // Stamp status and times
            model.Status = PurchaseStatus.Final;
            model.ReceivedAtUtc ??= DateTime.UtcNow;
            model.UpdatedAtUtc = DateTime.UtcNow;

            Log($"00.Validate+Normalize.DONE Subtotal={model.Subtotal:0.##} Disc={model.Discount:0.##} Tax={model.Tax:0.##} Other={model.OtherCharges:0.##} Grand={model.GrandTotal:0.##}");
            return lineList;
        }

        private async Task<Purchase> PersistFirstFinalAsync(
            PosClientDbContext db, Purchase model, List<PurchaseLine> lineList,
            string? user, Action<string> Log, CancellationToken ct)
        {
            Log("10.FirstFinalize.Header+Lines.START");

            model.CreatedAtUtc = DateTime.UtcNow;
            model.CreatedBy = user;
            model.UpdatedBy = user;
            model.DocNo = await EnsurePurchaseNumberAsync(db, model, ct);
            model.Revision = 0;

            db.Purchases.Add(model);
            await db.SaveChangesAsync(ct);

            foreach (var l in lineList)
            {
                l.Id = 0; l.PurchaseId = model.Id; l.Purchase = null;
            }
            await db.PurchaseLines.AddRangeAsync(lineList, ct);
            await db.SaveChangesAsync(ct);

            Log($"10.FirstFinalize.Header+Lines.DONE DocNo={model.DocNo} Id={model.Id}");
            return model;
        }

        private async Task<Purchase> PersistDraftToFinalAsync(
            PosClientDbContext db, Purchase existing, List<PurchaseLine> lineList,
            string? user, Action<string> Log, CancellationToken ct)
        {
            Log("22.DraftToFinal.START");

            // Replace lines
            db.PurchaseLines.RemoveRange(existing.Lines);
            await db.SaveChangesAsync(ct);

            foreach (var l in lineList)
            {
                l.Id = 0; l.PurchaseId = existing.Id; l.Purchase = null;
            }
            await db.PurchaseLines.AddRangeAsync(lineList, ct);
            await db.SaveChangesAsync(ct);

            // Promote to Final
            existing.Status = PurchaseStatus.Final;
            existing.ReceivedAtUtc ??= DateTime.UtcNow;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedBy = user;
            existing.Revision = 0;

            Log("22.DraftToFinal.DONE");
            return existing;
        }

        private (InventoryLocationType lt, int locId) ResolveExistingDestination(Purchase p)
        {
            if (p.LocationType == InventoryLocationType.Warehouse)
                return (InventoryLocationType.Warehouse, p.WarehouseId!.Value);
            return (InventoryLocationType.Outlet, p.OutletId!.Value);
        }

        private void UpdateHeaderForAmend(Purchase existing, Purchase incoming, List<PurchaseLine> normalized, string? user, Action<string> Log)
        {
            existing.PartyId = incoming.PartyId;
            existing.LocationType = incoming.LocationType;
            existing.OutletId = incoming.OutletId;
            existing.WarehouseId = incoming.WarehouseId;
            existing.PurchaseDate = incoming.PurchaseDate;
            existing.VendorInvoiceNo = incoming.VendorInvoiceNo;

            existing.DocNo = string.IsNullOrWhiteSpace(incoming.DocNo) ? (existing.DocNo ?? $"PO-{DateTime.UtcNow:yyyyMMdd}-???") : incoming.DocNo;

            existing.Subtotal = incoming.Subtotal;
            existing.Discount = incoming.Discount;
            existing.Tax = incoming.Tax;
            existing.OtherCharges = incoming.OtherCharges;
            existing.GrandTotal = incoming.GrandTotal;

            existing.Status = PurchaseStatus.Final;
            existing.ReceivedAtUtc = incoming.ReceivedAtUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedBy = user;
            existing.Revision = existing.Revision <= 0 ? 1 : existing.Revision + 1;

            Log($"HeaderUpdated DocNo={existing.DocNo} Revision={existing.Revision} NewGrand={existing.GrandTotal:0.##}");
        }

        private async Task ApplyDestinationMoveIfNeededAsync(
            PosClientDbContext db,
            Purchase existing,
            InventoryLocationType oldLt, int oldId,
            InventoryLocationType newLt, int newId,
            Action<string> Log, CancellationToken ct)
        {
            Log("21.MoveStockOnDestChange.START");

            var relevant = new[] { "Purchase", "PurchaseAmend" };
            var oldPosts = await db.StockEntries
                .Where(se => se.RefId == existing.Id
                          && se.LocationType == oldLt
                          && se.LocationId == oldId
                          && relevant.Contains(se.RefType))
                .ToListAsync(ct);

            if (oldPosts.Count == 0) { Log("21.MoveStockOnDestChange.SKIP (none)"); return; }

            foreach (var se in oldPosts)
            {
                se.LocationType = newLt;
                se.LocationId = newId;
                se.Note = "Relocated on amend (dest change)";
            }
            await db.SaveChangesAsync(ct);
            Log("21.MoveStockOnDestChange.DONE");
        }

        private sealed record AmendDelta(int ItemId, decimal QtyDelta, decimal UnitCost);

        private async Task<List<AmendDelta>> BuildAmendmentDeltasAsync(
            PosClientDbContext db,
            Purchase existing,
            List<PurchaseLine> nextLines,
            Action<string> Log, CancellationToken ct)
        {
            // Current effective = original lines + prior amend deltas
            var priorAmendQty = await db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "PurchaseAmend" && se.RefId == existing.Id)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct);

            var cur = existing.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key,
                              g => new {
                                  qty = g.Sum(x => x.Qty) + (priorAmendQty.TryGetValue(g.Key, out var q) ? q : 0m),
                                  unitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m
                              });

            var nxt = nextLines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key,
                              g => new { qty = g.Sum(x => x.Qty), unitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m });

            var ids = cur.Keys.Union(nxt.Keys).ToList();
            var deltas = new List<AmendDelta>(ids.Count);

            foreach (var id in ids)
            {
                var before = cur.TryGetValue(id, out var c) ? c.qty : 0m;
                var after = nxt.TryGetValue(id, out var n) ? n.qty : 0m;
                var dQty = after - before; // +IN, –OUT
                if (dQty == 0m) continue;

                var unitCost = nxt.TryGetValue(id, out var nMeta) ? nMeta.unitCost
                            : cur.TryGetValue(id, out var cMeta) ? cMeta.unitCost : 0m;

                deltas.Add(new AmendDelta(id, dQty, unitCost));
            }

            Log($"23.Amend.Deltas count={deltas.Count} netQty={deltas.Sum(x => x.QtyDelta):0.####}");
            return deltas;
        }

        private static async Task GuardNegativeOnHandForDeltasAsync(
            IInventoryReadService inv,
            List<AmendDelta> deltas,
            InventoryLocationType lt,
            int locId,
            Action<string> Log,
            CancellationToken ct)
        {
            if (deltas.Count == 0) return;
            var byItem = deltas.GroupBy(d => d.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.QtyDelta));
            var onhand = await inv.GetOnHandBulkAsync(byItem.Keys, lt, locId, DateTime.UtcNow, ct);

            var negHits = new List<string>();
            foreach (var kv in byItem)
            {
                var cur = onhand.TryGetValue(kv.Key, out var oh) ? oh : 0m;
                var next = cur + kv.Value;
                if (next < 0m)
                    negHits.Add($"Item#{kv.Key}: onHand={cur:0.####} + d={kv.Value:0.####} = {next:0.####}");
            }

            if (negHits.Count > 0)
                throw new InvalidOperationException("Amendment would make stock negative:\n" + string.Join("\n", negHits));
        }

        private async Task PostAmendmentStockAsync(
            PosClientDbContext db,
            Purchase existing,
            List<AmendDelta> deltas,
            Action<string> Log,
            CancellationToken ct)
        {
            var (lt, locId) = ResolveExistingDestination(existing);
            var entryOutletId = existing.OutletId ?? 0;
            var now = DateTime.UtcNow;

            foreach (var d in deltas)
            {
                db.StockEntries.Add(new StockEntry
                {
                    Ts = now,
                    OutletId = entryOutletId,
                    ItemId = d.ItemId,
                    QtyChange = d.QtyDelta,
                    UnitCost = d.UnitCost,
                    LocationType = lt,
                    LocationId = locId,
                    RefType = "PurchaseAmend",
                    RefId = existing.Id,
                    Note = $"Amend Rev {existing.Revision}"
                });
            }
            await db.SaveChangesAsync(ct);
            Log($"23.Amend.StockDelta POSTED count={deltas.Count}");
        }

        private async Task ApplySupplierCreditsSafelyAsync(
            PosClientDbContext db,
            Purchase persisted,
            string? user,
            Action<string> Log,
            CancellationToken ct)
        {
            try
            {
                await ApplySupplierCreditsAsync(db,
                    supplierId: persisted.PartyId,
                    outletId: persisted.OutletId,
                    purchase: persisted,
                    user: user ?? "system",
                    ct: ct);
                Log("Credits applied (best-effort).");
            }
            catch (Exception ex)
            {
                Log($"Credits WARN {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task PostGrossAsync(PosClientDbContext db, Purchase persisted, Action<string> Log, CancellationToken ct)
        {
            try
            {
                Log($"GL.PostPurchase (gross) Grand={persisted.GrandTotal:0.####}");
                await _gl.PostPurchaseAsync(db, persisted, ct);
            }
            catch (Exception ex)
            {
                Log($"GL WARN (gross) {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task PostRevisionDeltaAsync(PosClientDbContext db, Purchase existing, decimal deltaGrand, Action<string> Log, CancellationToken ct)
        {
            try
            {
                if (deltaGrand == 0m) { Log("GL.Skip revision delta (0)"); return; }
                Log($"GL.PostPurchaseRevision deltaGrand={deltaGrand:0.####}");
                await _gl.PostPurchaseRevisionAsync(db, existing, deltaGrand, ct);
            }
            catch (Exception ex)
            {
                Log($"GL WARN (revision) {ex.GetType().Name}: {ex.Message}");
            }
        }

        #endregion






        /// <summary>
        /// Auto-pick last UnitCost/Discount/TaxRate from latest FINAL purchase line of this item.
        /// </summary>
        public async Task<(decimal unitCost, decimal discount, decimal taxRate)?> GetLastPurchaseDefaultsAsync(int itemId, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var last = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.ItemId == itemId && _db.Purchases
                    .Where(p => p.Id == x.PurchaseId && p.Status == PurchaseStatus.Final)
                    .Any())
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.UnitCost, x.Discount, x.TaxRate })
                .FirstOrDefaultAsync(ct);

            if (last == null) return null;
            return (last.UnitCost, last.Discount, last.TaxRate);
        }

        // (Optional convenience) add/record a payment against a purchase
        public async Task<PurchasePayment> AddPaymentAsync(
    int purchaseId, PurchasePaymentKind kind, TenderMethod method, decimal amount, string? note,
    int outletId, int supplierId, int? tillSessionId, int? counterId, string user, int? bankAccountId = null,
    CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);
            var pay = await AddPaymentAsync(db, purchaseId, kind, method, amount, note,
                                            outletId, supplierId, tillSessionId, counterId, user, bankAccountId, ct);
            await tx.CommitAsync(ct);
            return pay;
        }



        public async Task<PurchasePayment> AddPaymentAsync(
     PosClientDbContext db,
     int purchaseId,
     PurchasePaymentKind kind,
     TenderMethod method,
     decimal amount,
     string? note,
     int outletId,
     int supplierId,
     int? tillSessionId, // unused
     int? counterId,     // unused
     string user,
     int? bankAccountId = null,
     CancellationToken ct = default,
     bool skipOverpayGuard = false)
        {
            if (amount <= 0m) throw new InvalidOperationException("Amount must be > 0.");

            var p = await db.Purchases.Include(x => x.Payments).FirstAsync(x => x.Id == purchaseId, ct);

            // Overpay guard
            if (!skipOverpayGuard)
            {
                var paid = p.Payments?.Where(x => x.IsEffective).Sum(x => x.Amount) ?? 0m;
                if (paid + amount > p.GrandTotal + 0.01m)
                    throw new InvalidOperationException("Payment exceeds invoice total.");
            }

            // Decide counter account here (NOT in GL)
            int counterAccountId;
            switch (method)
            {
                case TenderMethod.Cash:
                    if (p.LocationType != InventoryLocationType.Outlet || p.OutletId is null or 0)
                        throw new InvalidOperationException("Cash payments are allowed only for outlet purchases.");
                    counterAccountId = await _coa.GetTillAccountIdAsync(p.OutletId!.Value, ct)
                        ?? throw new InvalidOperationException("Outlet till account is not configured.");
                    break;

                case TenderMethod.Bank:
                case TenderMethod.Card:
                    if (bankAccountId is null)
                        throw new InvalidOperationException("Select an outlet bank account for bank/card payments.");
                    counterAccountId = bankAccountId.Value;
                    break;

                default:
                    throw new InvalidOperationException("Unsupported payment method for purchases.");
            }

            var pay = new PurchasePayment
            {
                PurchaseId = p.Id,
                SupplierId = supplierId,
                OutletId = p.LocationType == InventoryLocationType.Outlet ? p.OutletId : null,
                WarehouseId = p.LocationType == InventoryLocationType.Warehouse ? p.WarehouseId : null,
                TsUtc = DateTime.UtcNow,
                Kind = kind,
                Method = method,
                BankAccountId = bankAccountId,
                Amount = Math.Round(amount, 2),
                Note = note,
                IsEffective = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user
            };

            db.PurchasePayments.Add(pay);
            await db.SaveChangesAsync(ct); // need pay.Id for LinkedPaymentId

            // Post payment using the explicit counter account
            await _gl.PostPurchasePaymentAddedAsync(db, p, pay, counterAccountId, ct);

            // Update header
            p.CashPaid = p.Payments!.Where(x => x.IsEffective).Sum(x => x.Amount);
            p.CreditDue = Math.Max(0m, p.GrandTotal - p.CashPaid);
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user;
            await db.SaveChangesAsync(ct);

            return pay;
        }


        public async Task UpdatePaymentAsync(
    int paymentId, decimal newAmount, TenderMethod newMethod, string? newNote, string user, int? newBankAccountId = null, CancellationToken ct = default)
        {
            if (newAmount <= 0m) throw new InvalidOperationException("Amount must be > 0.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            var old = await db.PurchasePayments.Include(x => x.Purchase).FirstAsync(x => x.Id == paymentId, ct);
            if (!old.IsEffective) throw new InvalidOperationException("Payment already voided.");

            // Reverse old postings
            old.IsEffective = false;
            old.UpdatedAtUtc = DateTime.UtcNow;
            old.UpdatedBy = user;
            await db.SaveChangesAsync(ct);

            await _gl.PostPurchasePaymentReversalAsync(db, old.Id, ct);

            // Create new payment with proper counter account
            var p = await db.Purchases.Include(x => x.Payments).FirstAsync(x => x.Id == old.PurchaseId, ct);

            int counterAccountId;
            switch (newMethod)
            {
                case TenderMethod.Cash:
                    if (p.LocationType != InventoryLocationType.Outlet || p.OutletId is null or 0)
                        throw new InvalidOperationException("Cash payments are allowed only for outlet purchases.");
                    counterAccountId = await _coa.GetTillAccountIdAsync(p.OutletId!.Value, ct)
                        ?? throw new InvalidOperationException("Outlet till account is not configured.");
                    break;
                case TenderMethod.Bank:
                case TenderMethod.Card:
                    if (newBankAccountId is null)
                        throw new InvalidOperationException("Select an outlet bank account for bank/card payments.");
                    counterAccountId = newBankAccountId.Value;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported payment method for purchases.");
            }

            var npay = new PurchasePayment
            {
                PurchaseId = old.PurchaseId,
                SupplierId = old.SupplierId,
                OutletId = p.LocationType == InventoryLocationType.Outlet ? p.OutletId : null,
                WarehouseId = p.LocationType == InventoryLocationType.Warehouse ? p.WarehouseId : null,
                TsUtc = DateTime.UtcNow,
                Kind = old.Kind,
                Method = newMethod,
                BankAccountId = newBankAccountId,
                Amount = Math.Round(newAmount, 2),
                Note = newNote,
                IsEffective = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user
            };
            db.PurchasePayments.Add(npay);
            await db.SaveChangesAsync(ct);

            await _gl.PostPurchasePaymentAddedAsync(db, p, npay, counterAccountId, ct);

            p.CashPaid = p.Payments!.Where(x => x.IsEffective).Sum(x => x.Amount);
            p.CreditDue = Math.Max(0m, p.GrandTotal - p.CashPaid);
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user;
            await db.SaveChangesAsync(ct);
        }


        public async Task RemovePaymentAsync(int paymentId, string user, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var old = await db.PurchasePayments.Include(x => x.Purchase).FirstAsync(x => x.Id == paymentId, ct);
            if (!old.IsEffective) return;

            old.IsEffective = false;
            old.UpdatedAtUtc = DateTime.UtcNow;
            old.UpdatedBy = user;
            await db.SaveChangesAsync(ct);

            await _gl.PostPurchasePaymentReversalAsync(db, old.Id, ct);

            var p = await db.Purchases.Include(x => x.Payments).FirstAsync(x => x.Id == old.PurchaseId, ct);
            p.CashPaid = p.Payments!.Where(x => x.IsEffective).Sum(x => x.Amount);
            p.CreditDue = Math.Max(0m, p.GrandTotal - p.CashPaid);
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user;
            await db.SaveChangesAsync(ct);
        }


        // ----------------- Account resolvers (no App / no ICoaService) -----------------

        /// <summary>
        /// Returns the Outlet Cash-in-Hand account id using code pattern "111-{Outlet.Code}".
        /// Throws a clear error if not found (so you notice missing seeding).
        /// </summary>
        private async Task<int> ResolveCashAccountIdAsync(int outletId, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var cashCode = $"11101-{outlet.Code}";
            var cashId = await _db.Accounts.AsNoTracking()
                .Where(a => a.Code == cashCode && a.OutletId == outletId)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (cashId == 0)
                throw new InvalidOperationException($"Cash account '{cashCode}' not found for outlet #{outletId}. Make sure COA seeding created it.");

            return cashId;
        }

        /// <summary>
        /// Returns (or creates) the per-outlet "Supplier Advances" posting account under Assets.
        /// Code pattern: "113-{Outlet.Code}-ADV".
        /// If the asset header cannot be found, throws with a clear message.
        /// </summary>
        // NEW: reuse the current DbContext/transaction to avoid SQLite writer lock
        private static async Task<int> ResolveSupplierAdvancesAccountIdAsync(
            PosClientDbContext db, int outletId, CancellationToken ct = default)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);
            var code = $"113-{outlet.Code}-ADV";

            // Try existing
            var existingId = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && a.OutletId == outletId)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (existingId != 0) return existingId;

            // Find an Assets header for this outlet (preferred) or shared
            var assetHeader = await db.Accounts
                .Where(a => a.IsHeader && a.AllowPosting == false &&
                            a.Type == Pos.Domain.Entities.AccountType.Asset &&
                            (a.OutletId == outletId || a.OutletId == null))
                .OrderByDescending(a => a.OutletId) // prefer outlet-specific
                .FirstOrDefaultAsync(ct);

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

            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);
            return acc.Id;
        }



        public async Task<(Purchase purchase, List<PurchasePayment> payments)> GetWithPaymentsAsync(int purchaseId, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var purchase = await _db.Purchases.FirstAsync(p => p.Id == purchaseId, ct);
            var pays = await _db.PurchasePayments.Where(x => x.PurchaseId == purchaseId).OrderBy(x => x.TsUtc).ToListAsync(ct);
            return (purchase, pays);
        }

        // Helper: always compute from DB, ignore the navigation collection entirely
        static async Task<decimal> GetPaidSoFarAsync(PosClientDbContext db, int purchaseId, CancellationToken ct)
        {
            return await db.Set<PurchasePayment>()
                .Where(pp => pp.PurchaseId == purchaseId)
                .SumAsync(pp => (decimal?)pp.Amount, ct) ?? 0m;
        }


        private static async Task<int> GetPartyAccountIdAsync(PosClientDbContext db, int partyId, CancellationToken ct)
        {
            var acctId = await db.Parties
                .Where(p => p.Id == partyId)
                .Select(p => (int?)p.AccountId)
                .FirstOrDefaultAsync(ct);

            if (acctId is null)
                throw new InvalidOperationException($"PartyId={partyId} has no linked AccountId.");
            return acctId.Value;
        }

        private static async Task<decimal> GetApFromGlAsync(PosClientDbContext db, int purchaseId, int supplierAccountId, CancellationToken ct)
        {
            // AP = Credits (billings) – Debits (payments/downs/amendments) on the Supplier account
            var agg = await db.GlEntries
                .Where(e => e.DocId == purchaseId && e.AccountId == supplierAccountId)
                .GroupBy(_ => 1)
                .Select(g => new {
                    Cr = g.Sum(x => (decimal?)x.Credit) ?? 0m,
                    Dr = g.Sum(x => (decimal?)x.Debit) ?? 0m
                })
                .FirstOrDefaultAsync(ct);

            var cr = agg?.Cr ?? 0m;
            var dr = agg?.Dr ?? 0m;
            return cr - dr; // >0 we owe supplier; <0 supplier owes us
        }


        private static void ValidatePurchaseForSave(Purchase p, IEnumerable<PurchaseLine> lines)
        {
            if (p.PartyId == 0) throw new InvalidOperationException("Supplier is required.");
            if (lines == null || !lines.Any()) throw new InvalidOperationException("At least one line item is required.");

            switch (p.LocationType)
            {
                case InventoryLocationType.Outlet:
                    if (p.OutletId is null or 0) throw new InvalidOperationException("Outlet is required for outlet purchase.");
                    break;
                case InventoryLocationType.Warehouse:
                    if (p.WarehouseId is null or 0) throw new InvalidOperationException("Warehouse is required for warehouse purchase.");
                    break;
                default:
                    throw new InvalidOperationException("Invalid purchase location type.");
            }
        }




        // Pos.Persistence/Services/PurchasesService.cs
        public async Task<Purchase> FinalizeReceiveAsync(
    Purchase purchase,
    IEnumerable<PurchaseLine> lines,
    IEnumerable<(TenderMethod method, decimal amount, string? note)> onReceivePayments,
    int outletId,
    int supplierId,
    int? tillSessionId,
    int? counterId,
    string user,
    CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var trx = await db.Database.BeginTransactionAsync(ct);

            var isNew = purchase.Id == 0;
            var now = DateTime.UtcNow;

            if (isNew)
            {
                // 1) insert header + lines (compute totals centrally like you already do)
                // ... (your existing normalized save)
                purchase.Status = PurchaseStatus.Final;
                purchase.UpdatedAtUtc = now; purchase.UpdatedBy = user;
                await db.SaveChangesAsync(ct);

                // 2) persist on-receive payments (effective)
                foreach (var (m, amt, note) in onReceivePayments)
                    await AddPaymentAsync(db, purchase.Id, PurchasePaymentKind.OnReceive, m, amt, note, outletId, supplierId, tillSessionId, counterId, user, null, ct, skipOverpayGuard: false);

                // 3) single GL post (gross + effective payments)
                await _gl.PostPurchaseAsync(db, purchase, ct);

                await trx.CommitAsync(ct);
                return purchase;
            }
            else
            {
                // AMENDMENT:
                var old = await db.Purchases
                    .AsNoTracking()
                    .FirstAsync(x => x.Id == purchase.Id, ct);

                // Update lines + recompute totals…
                // (return-only code omitted; stick to delta on GrandTotal)
                var deltaGrand = Math.Round(purchase.GrandTotal - old.GrandTotal, 2);
                purchase.UpdatedAtUtc = now; purchase.UpdatedBy = user;
                await db.SaveChangesAsync(ct);

                // Only delta gross
                if (deltaGrand != 0m)
                    await _gl.PostPurchaseRevisionAsync(db, purchase, deltaGrand, ct);

                // Any payment edits/adds/removes should be invoked via dedicated methods from the UI.
                // (No summed payment posting here — we post per-payment deltas above.)

                await trx.CommitAsync(ct);
                return purchase;
            }
        }




        // ---------- PURCHASE RETURNS (with refunds & supplier credit) ----------
        // IPurchasesService implementation (matches the interface)
        public async Task<PurchaseReturnDraft> BuildReturnDraftAsync(int originalPurchaseId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await BuildReturnDraftAsync(db, originalPurchaseId, ct);
        }

        /// <summary>
        /// Build a draft for a return from a FINAL purchase.
        /// Pre-fills remaining-allowed quantities per line.
        /// </summary>
        /// 
        public async Task<PurchaseReturnDraft> BuildReturnDraftAsync(PosClientDbContext _db, int originalPurchaseId, CancellationToken ct = default)
        {
            //await using var _db = await _dbf.CreateDbContextAsync(ct);
            var p = await _db.Purchases
                .Include(x => x.Lines).ThenInclude(l => l.Item)
                .Include(x => x.Party)
                .FirstAsync(x => x.Id == originalPurchaseId && x.Status == PurchaseStatus.Final && !x.IsReturn, ct);

            // Already returned per original line (stored as negative qty; convert to positive for math)
            var already = await _db.Purchases
                .Where(r => r.IsReturn && r.RefPurchaseId == originalPurchaseId && r.Status != PurchaseStatus.Voided)
                .SelectMany(r => r.Lines)
                .Where(l => l.RefPurchaseLineId != null)
                .GroupBy(l => l.RefPurchaseLineId!.Value)
                .Select(g => new { OriginalLineId = g.Key, ReturnedAbs = Math.Abs(g.Sum(z => z.Qty)) })
                .ToListAsync(ct);

            var returnedByLine = already.ToDictionary(x => x.OriginalLineId, x => x.ReturnedAbs);

            return new PurchaseReturnDraft
            {
                PartyId = p.PartyId,
                LocationType = p.LocationType,
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
        public Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, CancellationToken ct = default)
        => SaveReturnAsync(model, lines, user, refunds: null, tillSessionId: null, counterId: null, ct);


        /// <summary>
        /// Save a FINAL purchase return with optional refunds and auto-credit creation.
        /// </summary>
        public async Task<Purchase> SaveReturnAsync(Purchase model, IEnumerable<PurchaseLine> lines, string? user = null, IEnumerable<SupplierRefundSpec>? refunds = null, int? tillSessionId = null, int? counterId = null, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
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
                var draft = await BuildReturnDraftAsync(_db,model.RefPurchaseId!.Value);
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

            using var tx = await _db.Database.BeginTransactionAsync(ct);

            if (model.Id == 0)
            {
                model.CreatedAtUtc = DateTime.UtcNow;
                model.CreatedBy = user;
                model.DocNo = await EnsureReturnNumberAsync(_db, model, CancellationToken.None);
                model.Revision = 0;

                _db.Purchases.Add(model);
                await _db.SaveChangesAsync(ct);

                foreach (var l in lineList)
                {
                    l.Id = 0;
                    l.PurchaseId = model.Id;
                    l.Purchase = null;
                }
                await _db.PurchaseLines.AddRangeAsync(lineList, ct);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                // Amending an existing return
                var existing = await _db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == model.Id);

                if (!existing.IsReturn)
                    throw new InvalidOperationException("Cannot overwrite a purchase with a return.");

                var wasFinal = existing.Status == PurchaseStatus.Final;

                existing.PartyId = model.PartyId;
                existing.LocationType = model.LocationType;
                existing.OutletId = model.OutletId;
                existing.WarehouseId = model.WarehouseId;
                existing.PurchaseDate = model.PurchaseDate;
                existing.VendorInvoiceNo = model.VendorInvoiceNo;

                existing.RefPurchaseId = model.RefPurchaseId; // may be null for free-form

                existing.DocNo = string.IsNullOrWhiteSpace(model.DocNo)
                    ? await EnsureReturnNumberAsync(_db, existing, CancellationToken.None)
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
                await _db.SaveChangesAsync(ct);

                foreach (var l in lineList)
                {
                    l.Id = 0; l.PurchaseId = existing.Id; l.Purchase = null;
                }
                existing.Lines = lineList;

                model = existing;
            }

            await _db.SaveChangesAsync(ct);
            //await PostPurchaseReturnStockAsync(model, lineList, user ?? "system");
            await PostPurchaseReturnStockAsync(_db, model, lineList, user ?? "system", ct);

            // === CHANGED: Only auto-apply against original if one exists.
            decimal appliedToOriginal = 0m;
            if (hasRefPurchase)
            {
                try
                {
                    appliedToOriginal = await AutoApplyReturnToOriginalAsync_ReturnApplied(_db,model, user ?? "system");
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
                        _db,
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
                await _db.SaveChangesAsync(ct);
                await _outbox.EnqueueUpsertAsync(_db, credit, default);
                await _db.SaveChangesAsync(ct);

            }

            // TODO: Post stock ledger with negative deltas for all lines:
            // - Location: OutletId or WarehouseId depending on model.TargetType
            // - Delta: l.Qty (already negative)
            // - Valuation: l.UnitCost (free-form uses edited cost; referenced uses locked cost)

            await tx.CommitAsync(ct);
            await _outbox.EnqueueUpsertAsync(_db, model, default);
            await _db.SaveChangesAsync(ct);

            return model;
        }

        public Task<decimal> GetOnHandAsync(int itemId, InventoryLocationType target, int? outletId, int? warehouseId, CancellationToken ct = default)
        {
            if (itemId <= 0) return Task.FromResult(0m);

            InventoryLocationType locType;
            int locId;

            if (target == InventoryLocationType.Outlet)
            {
                if (outletId is not int o || o <= 0) return Task.FromResult(0m);
                locType = InventoryLocationType.Outlet;
                locId = o;
            }
            else
            {
                if (warehouseId is not int w || w <= 0) return Task.FromResult(0m);
                locType = InventoryLocationType.Warehouse;
                locId = w;
            }

            // Centralized logic (strict-before + clamp)
            return _inv.GetOnHandAtLocationAsync(itemId, locType, locId, DateTime.UtcNow, ct);
        }




        private async Task<string> EnsureReturnNumberAsync(PosClientDbContext _db, Purchase p, CancellationToken ct)
        {
            //await using var _db = await _dbf.CreateDbContextAsync(ct);
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

            if (p.LocationType == InventoryLocationType.Outlet)
            {
                if (p.OutletId is null || p.OutletId <= 0)
                    throw new InvalidOperationException("Outlet required.");
            }
            else if (p.LocationType == InventoryLocationType.Warehouse)
            {
                if (p.WarehouseId is null || p.WarehouseId <= 0)
                    throw new InvalidOperationException("Warehouse required.");
            }
        }

        // ---------- Convenience Queries for UI Pickers ----------

        public async Task<List<Purchase>> ListHeldAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Purchases
                .AsNoTracking()
                .Include(p => p.Party)
                .Where(p => p.Status == PurchaseStatus.Draft)
                .OrderByDescending(p => p.PurchaseDate)
                .ToListAsync(ct);
        }


        public async Task<List<Purchase>> ListPostedAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Purchases
                .AsNoTracking()
                .Include(p => p.Party)
                .Where(p => p.Status == PurchaseStatus.Final)
                .OrderByDescending(p => p.ReceivedAtUtc ?? p.PurchaseDate)
                .ToListAsync(ct);
        }


        public async Task<Purchase> LoadWithLinesAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Purchases
                .Include(p => p.Lines)
                .Include(p => p.Party)
                .FirstAsync(p => p.Id == id, ct);
        }


        // ---------- INTERNAL CREDIT/REFUND HELPERS ----------

        /// <summary>
        /// Non-cash adjustment on original purchase for return value. Returns amount applied.
        /// </summary>
        private async Task<decimal> AutoApplyReturnToOriginalAsync_ReturnApplied(PosClientDbContext _db, Purchase savedReturn, string user, CancellationToken ct=default)
        {
            //await using var _db = await _dbf.CreateDbContextAsync(ct);
            if (!savedReturn.IsReturn || savedReturn.RefPurchaseId is null or <= 0)
                return 0m;

            var original = await _db.Purchases
                .Include(p => p.Payments)
                .FirstAsync(p => p.Id == savedReturn.RefPurchaseId.Value, ct);

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
        // Inside PurchasesService
        private async Task RecordSupplierRefundAsync(
            PosClientDbContext db,
            int returnId,
            int supplierId,
            int outletId,
            int? tillSessionId,
            int? counterId,
            SupplierRefundSpec refund,
            string user,
            CancellationToken ct = default)
        {
            var amt = Math.Round(refund.Amount, 2);
            if (amt <= 0) throw new InvalidOperationException("Refund amount must be > 0.");

            // 1) Cash drawer snapshot (for till reports)
            if (refund.Method == TenderMethod.Cash)
            {
                var cash = new CashLedger
                {
                    OutletId = outletId,
                    CounterId = counterId,
                    TillSessionId = tillSessionId,
                    TsUtc = DateTime.UtcNow,
                    Delta = +amt,                      // CASH IN
                    RefType = "PurchaseReturnRefund",
                    RefId = returnId,
                    Note = refund.Note,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = user
                };
                db.CashLedgers.Add(cash);
                await db.SaveChangesAsync(ct);
            }

            // 2) GL posting for the refund (Cash or Bank)
            //    Dr Till/Bank, Cr Supplier (against the PurchaseReturn document)
            if (refund.Method == TenderMethod.Cash || refund.Method == TenderMethod.Bank)
            {
                // Resolve Supplier account (Party.AccountId -> Accounts.Id)
                var supplierAccountId = await db.Parties.AsNoTracking()
                    .Where(p => p.Id == supplierId)
                    .Select(p => p.AccountId)
                    .FirstOrDefaultAsync(ct);
                if (supplierAccountId is null or <= 0)
                    throw new InvalidOperationException($"Supplier #{supplierId} does not have a linked ledger account.");

                int debitAccountId;     // Till or Bank
                string debitMemo;

                if (refund.Method == TenderMethod.Cash)
                {
                    // Till account: "11101-{Outlet.Code}"
                    var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);
                    var cashCode = $"11101-{outlet.Code}";
                    debitAccountId = await db.Accounts.AsNoTracking()
                        .Where(a => a.Code == cashCode && a.OutletId == outletId)
                        .Select(a => a.Id)
                        .FirstOrDefaultAsync(ct);
                    if (debitAccountId == 0)
                        throw new InvalidOperationException($"Till account not found for outlet #{outletId}.");
                    debitMemo = "Supplier refund (Cash/Till)";
                }
                else
                {
                    // Bank refund: use configured Purchase Bank Account for the outlet
                    var inv = await db.InvoiceSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.OutletId == outletId, ct);
                    if (inv?.PurchaseBankAccountId is null)
                        throw new InvalidOperationException("Bank refund requires a configured Purchase Bank Account in Invoice Settings.");
                    debitAccountId = inv.PurchaseBankAccountId.Value;
                    debitMemo = "Supplier refund (Bank)";
                }

                var ts = DateTime.UtcNow;

                db.GlEntries.AddRange(
                    new Pos.Domain.Accounting.GlEntry
                    {
                        TsUtc = ts,
                        OutletId = outletId,
                        AccountId = debitAccountId,
                        Debit = amt,
                        Credit = 0m,
                        DocType = Pos.Domain.Accounting.GlDocType.PurchaseReturn,
                        DocId = returnId,
                        Memo = debitMemo
                    },
                    new Pos.Domain.Accounting.GlEntry
                    {
                        TsUtc = ts,
                        OutletId = outletId,
                        AccountId = supplierAccountId!.Value,
                        Debit = 0m,
                        Credit = amt,
                        DocType = Pos.Domain.Accounting.GlDocType.PurchaseReturn,
                        DocId = returnId,
                        Memo = "Reduce Supplier (refund)"
                    }
                );

                await db.SaveChangesAsync(ct);

                // Optional but recommended: enqueue for sync
                await _outbox.EnqueueUpsertAsync(db, new Pos.Domain.Accounting.GlEntry { Id = 0 /* placeholder: framework inspects DbContext */ }, default);
            }
            else
            {
                // TenderMethod.Other: no GL, by design (non-cash adjustment)
            }
        }

        
        public async Task ApplySupplierCreditsAsync(int supplierId, int? outletId, Purchase purchase, string user, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct); await ApplySupplierCreditsAsync(db, supplierId, outletId, purchase, user, ct);
        }
        /// <summary>
        /// Consume available SupplierCredits as non-cash adjustments on a purchase.
        /// Prefers outlet-scoped credits, then global credits; oldest first.
        /// </summary>
        private async Task<decimal> ApplySupplierCreditsAsync(PosClientDbContext db,
            int supplierId, int? outletId, Purchase purchase, string user, CancellationToken ct=default)
        {
            //await using var _db = await _dbf.CreateDbContextAsync(ct);
            // Determine how much we still need to cover
            var currentPaid = purchase.Payments.Sum(p => p.Amount);
            var need = Math.Max(0, purchase.GrandTotal - currentPaid);
            if (need <= 0) return 0m;

            // Pull credits (oldest first). Prefer outlet-specific first, then global.
            var credits = await db.SupplierCredits
                .Where(c => c.SupplierId == supplierId && (c.OutletId == outletId || c.OutletId == null) && c.Amount > 0)
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync(ct);

            decimal used = 0m;
            var touched = new List<SupplierCredit>(); // track credits we modify

            foreach (var c in credits)
            {
                if (need <= 0) break;
                var take = Math.Min(c.Amount, need);
                if (take <= 0) continue;

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

                c.Amount = Math.Round(c.Amount - take, 2);
                touched.Add(c); // mark changed
                used += take;
                need -= take;
            }


            // Remove zeroed credits to keep table clean
            var zeroed = credits.Where(c => c.Amount <= 0.0001m).ToList();
            if (zeroed.Count > 0)
                db.SupplierCredits.RemoveRange(zeroed);

            await db.SaveChangesAsync(ct);
            // === SYNC: updated credits + purchase balance
            foreach (var c in touched.Where(x => x.Amount > 0.0001m))
            {
                await _outbox.EnqueueUpsertAsync(db, c, default);
            }
            // For removed credits, you can skip (treat as fully consumed). If you want to mirror deletions,
            // consider a soft-delete flag in the future.
            await _outbox.EnqueueUpsertAsync(db, purchase, default);
            await db.SaveChangesAsync(ct);

            return used;
        }

        // using Microsoft.EntityFrameworkCore;
        // using Pos.Domain.Entities;
        // ...

        //private async Task PostPurchaseStockAsync(Purchase model, IEnumerable<PurchaseLine> lines, string user, CancellationToken ct = default)
         private static async Task PostPurchaseStockAsync(
             PosClientDbContext db,
             Purchase model,
             IEnumerable<PurchaseLine> lines,
             string user,
             CancellationToken ct = default)
         {
             var locType = model.LocationType == InventoryLocationType.Warehouse
                 ? InventoryLocationType.Warehouse
                 : InventoryLocationType.Outlet;
             var locId = model.LocationType == InventoryLocationType.Warehouse
                 ? model.WarehouseId!.Value
                 : model.OutletId!.Value;

             // If this purchase was already FINAL and is being amended, remove previous postings
             var prior = await db.StockEntries
                 .Where(se => se.RefType == "Purchase" && se.RefId == model.Id)
                 .ToListAsync(ct);
             if (prior.Count > 0)
             {
                 db.StockEntries.RemoveRange(prior);
                 await db.SaveChangesAsync(ct);
             }

             var now = DateTime.UtcNow;
             foreach (var l in lines)
             {
                 db.StockEntries.Add(new StockEntry
                 {
                     Ts = now,
                     ItemId = l.ItemId,
                     LocationType = locType,
                     LocationId = locId,
                     QtyChange = l.Qty,        // Purchase FINAL = IN
                     UnitCost = l.UnitCost,    // keep for costing/audit
                     RefType = "Purchase",
                     RefId = model.Id,
                     Note = model.VendorInvoiceNo
                 });
             }
        await db.SaveChangesAsync(ct);
         }

        // Pos.Persistence/Services/PurchasesService.cs
        // Reuse the caller's DbContext/transaction to avoid SQLite writer locks
        // Reuse the caller's DbContext/transaction to avoid SQLite writer locks
        private static async Task PostPurchaseReturnStockAsync(
            PosClientDbContext db,
            Purchase model,
            IEnumerable<PurchaseLine> lines,
            string user,
            CancellationToken ct = default)
        {
            // ---- Resolve where to post OUT (debit) ----
            InventoryLocationType locType;
            int locId;
            int entryOutletId; // StockEntry.OutletId (use 0 for pure-warehouse)

            if (model.RefPurchaseId is int rid && rid > 0)
            {
                // WITH-INVOICE: reduce where the original purchase was received
                var orig = await db.Purchases.AsNoTracking().FirstAsync(p => p.Id == rid, ct);

                if (orig.LocationType == InventoryLocationType.Warehouse)
                {
                    locType = InventoryLocationType.Warehouse;
                    locId = orig.WarehouseId!.Value;
                    entryOutletId = 0;
                }
                else
                {
                    locType = InventoryLocationType.Outlet;
                    locId = orig.OutletId!.Value;
                    entryOutletId = orig.OutletId!.Value;
                }
            }
            else
            {
                // WITHOUT-INVOICE: reduce at the user-selected header source
                if (model.LocationType == InventoryLocationType.Warehouse)
                {
                    locType = InventoryLocationType.Warehouse;
                    locId = model.WarehouseId!.Value;
                    entryOutletId = 0;
                }
                else
                {
                    locType = InventoryLocationType.Outlet;
                    locId = model.OutletId!.Value;
                    entryOutletId = model.OutletId!.Value;
                }
            }

            // Remove any prior stock entries for this return, then add fresh rows
            var prior = await db.StockEntries
                .Where(se => se.RefType == "PurchaseReturn" && se.RefId == model.Id)
                .ToListAsync(ct);

            if (prior.Count > 0)
            {
                db.StockEntries.RemoveRange(prior);
                await db.SaveChangesAsync(ct);
            }

            var now = DateTime.UtcNow;

            foreach (var l in lines)
            {
                // We allow zero-qty lines to be ignored, but DO NOT skip negative (return) qty.
                var qty = l.Qty;                 // returns are already negative
                if (qty == 0m) continue;

                db.StockEntries.Add(new StockEntry
                {
                    Ts = now,
                    OutletId = entryOutletId,          // 0 for warehouse
                    ItemId = l.ItemId,
                    LocationType = locType,
                    LocationId = locId,
                    QtyChange = qty,                    // negative for return
                    UnitCost = l.UnitCost,
                    RefType = "PurchaseReturn",
                    RefId = model.Id,
                    Note = model.VendorInvoiceNo
                });
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task VoidPurchaseAsync(int purchaseId, string reason, string? user = null, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            // Load the purchase (non-return)
            var p = await _db.Purchases
                .AsNoTracking()
                .FirstAsync(x => x.Id == purchaseId && !x.IsReturn, ct);

            if (p.Status == PurchaseStatus.Voided) return;

            // Resolve the exact ledger location used by postings
            var locType = p.LocationType == InventoryLocationType.Warehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;
            var locId = p.LocationType == InventoryLocationType.Warehouse
                ? p.WarehouseId!.Value
                : p.OutletId!.Value;

            // All postings tied to this purchase that must be removed
            // IMPORTANT: include both "Purchase" and "PurchaseAmend"
            var postings = await _db.StockEntries
                .Where(se => se.RefId == p.Id &&
                      (se.RefType == "Purchase" || se.RefType == "PurchaseAmend" || se.RefType == "PurchaseMove"))
                .ToListAsync(ct);

            // Nothing posted? Just mark void and exit.
            if (postings.Count == 0)
            {
                var entity = await _db.Purchases.FirstAsync(x => x.Id == p.Id);
                entity.Status = PurchaseStatus.Voided;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                entity.UpdatedBy = user;
                await _db.SaveChangesAsync(ct);

                // === SYNC: voided purchase
                await _outbox.EnqueueUpsertAsync(_db, entity, default);
                await _db.SaveChangesAsync(ct);
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

            var onhandByItem = await _inv.GetOnHandBulkAsync(itemIds, locType, locId, DateTime.UtcNow, ct);

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
            using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.StockEntries.RemoveRange(postings);

            var entity2 = await _db.Purchases.FirstAsync(x => x.Id == p.Id);
            entity2.Status = PurchaseStatus.Voided;
            entity2.UpdatedAtUtc = DateTime.UtcNow;
            entity2.UpdatedBy = user;
            // (Optional) record audit row if you keep one
            //_db.AuditLogs.Add(new AuditLog { ... });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            // === SYNC: voided purchase
            var voided = await _db.Purchases.AsNoTracking().FirstAsync(x => x.Id == p.Id, ct);
            await _outbox.EnqueueUpsertAsync(_db, voided, default);
            await _db.SaveChangesAsync(ct);

        }


        public async Task VoidReturnAsync(int returnId, string reason, string? user = null, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var r = await _db.Purchases.Include(x => x.Lines)
                                       .FirstAsync(x => x.Id == returnId && x.IsReturn, ct);
            if (r.Status == PurchaseStatus.Voided) return;
            var postings = await _db.StockEntries
                .Where(se => se.RefType == "PurchaseReturn" && se.RefId == r.Id)
                .ToListAsync(ct);
            if (postings.Count > 0) _db.StockEntries.RemoveRange(postings);

            r.Status = PurchaseStatus.Voided;
            r.UpdatedAtUtc = DateTime.UtcNow;
            r.UpdatedBy = user;
            await _db.SaveChangesAsync(ct);
            // === SYNC: voided return
            await _outbox.EnqueueUpsertAsync(_db, r, default);
            await _db.SaveChangesAsync(ct);

        }

        public async Task<List<PurchaseLineEffective>> GetEffectiveLinesAsync(int purchaseId, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
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
                .ToDictionaryAsync(x => x.ItemId, x => x, ct);
            // prior amendment deltas (qty only)
            var amendQty = await _db.StockEntries
                .AsNoTracking()
                .Where(se => se.RefType == "PurchaseAmend" && se.RefId == purchaseId)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct);
            // build effective map
            var ids = baseLines.Keys.Union(amendQty.Keys).ToList();
            var effective = new List<PurchaseLineEffective>(ids.Count);
            // minimal item meta
            var itemsMeta = await _db.Items
                .AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, i.Name, i.Price, i.DefaultTaxRatePct })
                .ToDictionaryAsync(x => x.Id, x => x, ct);

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

        public async Task<decimal> GetRemainingReturnableQtyAsync(int purchaseLineId, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            // original line qty (may be negative)
            var orig = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.Id == purchaseLineId)
                .Select(x => (decimal?)x.Qty)
                .FirstOrDefaultAsync(ct);

            if (orig is null) return 0m;
            // SUM of positive magnitudes for all returns that reference this line
            // Use ternary to emulate ABS() and nullable SUM to handle empty set => 0
            var returned = await _db.PurchaseLines
                .AsNoTracking()
                .Where(x => x.RefPurchaseLineId == purchaseLineId)
                .Select(x => (decimal?)(x.Qty < 0 ? -x.Qty : x.Qty))
                .SumAsync(ct) ?? 0m;

            var origAbs = orig.Value < 0 ? -orig.Value : orig.Value;
            var remaining = Math.Max(0m, origAbs - returned);
            return remaining;
        }

        // --- Bank configuration & pickers (outlet-scoped) ---
        public async Task<bool> IsPurchaseBankConfiguredAsync(int outletId, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var s = await _db.InvoiceSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutletId == outletId, ct);
            return s?.PurchaseBankAccountId != null;
        }

        public async Task<List<Account>> ListBankAccountsForOutletAsync(int outletId, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            return await _db.Accounts.AsNoTracking()
                .Where(a => a.OutletId == outletId
                         && a.AllowPosting
                         && !a.IsHeader
                         && (a.Name.Contains("Bank") || a.Code.StartsWith("101")))
                .OrderBy(a => a.Name)
                .ToListAsync(ct);
        }

        public async Task<int?> GetConfiguredPurchaseBankAccountIdAsync(int outletId, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var s = await _db.InvoiceSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutletId == outletId, ct);
            return s?.PurchaseBankAccountId;
        }

        // --- Lightweight lookups the view needed previously via _db.Set<T> ---
        public async Task<string?> GetPartyNameAsync(int partyId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Set<Party>().AsNoTracking()
                .Where(x => x.Id == partyId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
        }


        public async Task<Dictionary<int, (string sku, string name)>> GetItemsMetaAsync(IEnumerable<int> itemIds, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var ids = itemIds.Distinct().ToArray();
            var list = await db.Items.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, i.Name })
                .ToListAsync(ct);

            return list.ToDictionary(x => x.Id, x => (x.Sku ?? "", x.Name ?? $"Item #{x.Id}"));
        }

        // --- Draft loader used by Resume (status-guarded) ---
        public async Task<Purchase?> LoadDraftWithLinesAsync(int id, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            return await _db.Purchases
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == PurchaseStatus.Draft, ct);
        }

        // --- Payment edits (moved out of the View) ---
        public async Task UpdatePaymentAsync(int paymentId, decimal newAmount, TenderMethod newMethod, string? newNote, string user, CancellationToken ct=default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            var pay = await _db.PurchasePayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
            if (pay is null) throw new InvalidOperationException($"Payment #{paymentId} not found.");

            if (newAmount <= 0) throw new InvalidOperationException("Amount must be > 0.");
            pay.Amount = Math.Round(newAmount, 2);
            pay.Method = newMethod;
            pay.Note = string.IsNullOrWhiteSpace(newNote) ? null : newNote.Trim();
            pay.UpdatedAtUtc = DateTime.UtcNow;
            pay.UpdatedBy = user;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<PurchasePayment>> GetPaymentsAsync(int purchaseId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var rows = await db.PurchasePayments
                .AsNoTracking()
                .Where(p => p.PurchaseId == purchaseId)
                .OrderBy(p => p.Id)
                .ToListAsync(ct);

            return rows;
        }

        

        public async Task<Purchase?> LoadReturnWithLinesAsync(int returnId, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            return await _db.Purchases
                .Include(p => p.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == returnId && p.IsReturn, ct);
        }




    }
    // ---------- Simple DTOs for Return Draft ----------
    
}
