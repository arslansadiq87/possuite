// Pos.Persistence/Services/PurchasesService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Settings;
using Pos.Domain.Models.Purchases;
using Pos.Domain.Services;
using Pos.Persistence.Sync;
using Pos.Domain.Models.Inventory;
namespace Pos.Persistence.Services
{
    /// <summary>
    /// Fresh, rule-true purchase pipeline:
    /// - Draft Save  : post only payments (advance). No stock. No gross AP.
    /// - Post & Save : post stock + gross AP; also post any payments (delta).
    /// - Amend       : compute strict deltas (stock + GL + payments). Enforce no-negative-stock.
    /// - Void        : reverse stock and payments (IsEffective=false + reversing rows). Guard negatives.
    /// All postings use GlEntry.IsEffective with delta chains under ChainId = Purchase.PublicId.
    /// </summary>
    public sealed class PurchasesService : IPurchasesService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IInventoryReadService _inv;
        private readonly IStockGuard _stockGuard;
        private readonly ICoaService _coa;
        private readonly IGlPostingService _gl;
        private readonly IOutboxWriter _outbox;

        public PurchasesService(
            IDbContextFactory<PosClientDbContext> dbf,
            IInventoryReadService inv,
            IStockGuard stockGuard,
            ICoaService coa,
            IGlPostingService gl,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _inv = inv;
            _stockGuard = stockGuard;
            _coa = coa;
            _gl = gl;
            _outbox = outbox;
        }

        // -------------------------------
        // Public API (rule-aligned)
        // -------------------------------

        // DRAFT SAVE — payments only (advance). No stock. No gross AP yet.
        public async Task<Purchase> SaveDraftAsync(
            Purchase draft,
            IEnumerable<PurchaseLine> lines,
            string? user = null,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);

            NormalizeAndCompute(draft, lines);
            draft.Status = PurchaseStatus.Draft;
            draft.DocNo = null;
            draft.ReceivedAtUtc = null;
            draft.UpdatedAtUtc = DateTime.UtcNow;
            draft.UpdatedBy = user;

            UpsertHeaderAndLines(db, draft, lines);

            await PostPaymentsDeltaAsync(db, draft, user, ct);

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, draft, ct);
            await tx.CommitAsync(ct);
            return draft;
        }

        // POST & SAVE — stock + gross AP + payments (delta)
        public async Task<Purchase> FinalizeReceiveAsync(
            Purchase purchase,
            IEnumerable<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> onReceivePayments,
            int outletId, int supplierId, int? tillSessionId, int? counterId, string user,
            CancellationToken ct = default)
        {
            var trace = $"[FinalizeReceive] {Guid.NewGuid():N}";
            global::System.Diagnostics.Debug.WriteLine($"{trace} ENTER user={user} outlet={outletId} supplier={supplierId}");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var isNew = purchase.Id == 0;
                var nowUtc = DateTime.UtcNow;
                Purchase? existing = null;
                if (!isNew)
                {
                    existing = await db.Purchases
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == purchase.Id, ct)
                        ?? throw new InvalidOperationException("Purchase not found for amendment.");

                    var currentRevision = existing.Revision;
                    var wasDraft = existing.Status == PurchaseStatus.Draft;
                    var becomingFinal = purchase.Status == PurchaseStatus.Final;

                    if (wasDraft && becomingFinal && currentRevision == 0)
                        purchase.Revision = currentRevision;
                    else
                        purchase.Revision = currentRevision + 1;

                    if (purchase.PublicId == Guid.Empty)
                        purchase.PublicId = existing.PublicId;       
                    
                    if (string.IsNullOrWhiteSpace(purchase.DocNo))
                        purchase.DocNo = existing.DocNo;

                    if (purchase.PublicId == Guid.Empty)
                        purchase.PublicId = existing.PublicId;

                    purchase.CreatedAtUtc = existing.CreatedAtUtc;
                    purchase.CreatedBy = existing.CreatedBy;
                }
                else
                {
                    purchase.Revision = 0;

                }

                global::System.Diagnostics.Debug.WriteLine($"{trace} 01.Normalize+Compute");
                NormalizeAndCompute(purchase, lines);

                purchase.Status = PurchaseStatus.Final;
                purchase.UpdatedAtUtc = nowUtc;
                purchase.UpdatedBy = user;

                global::System.Diagnostics.Debug.WriteLine($"{trace} 02.UpsertHeaderAndLines");
                UpsertHeaderAndLines(db, purchase, lines);

        
                if (isNew)
                {
                    // ✅ ensure audit on brand-new header
                    purchase.CreatedAtUtc = nowUtc;
                    purchase.CreatedBy = user;
                    global::System.Diagnostics.Debug.WriteLine($"{trace} 03.Numbering");
                    purchase.DocNo ??= await NextPurchaseNoAsync(db, outletId, ct);
                    purchase.ReceivedAtUtc = nowUtc;
                }

                // 03b.SAVE HEADER (+LINES) so purchase.Id is non-zero for FKs
                System.Diagnostics.Debug.WriteLine($"{trace} 03b.SaveHeader");
                await db.SaveChangesAsync(ct);

                // right before you build/apply stock deltas
                var unitCostByItem = BuildPerItemUnitCostMap(purchase);

                global::System.Diagnostics.Debug.WriteLine($"{trace} 04.BuildPurchaseStockDeltas");
                var stockDeltas = BuildPurchaseStockDeltas(db, purchase);

                global::System.Diagnostics.Debug.WriteLine($"{trace} 05.StockGuard");
                await _stockGuard.EnsureNoNegativeAtLocationAsync(stockDeltas, nowUtc, ct);

                global::System.Diagnostics.Debug.WriteLine($"{trace} 06.ApplyStock");
                await ApplyStockAsync(db, purchase, stockDeltas, unitCostByItem, nowUtc, user, ct);


                global::System.Diagnostics.Debug.WriteLine($"{trace} 07.GL Gross (Inventory DR / Supplier CR)");
                await ((IGlPostingServiceDb)_gl).PostPurchaseAsync(db, purchase, ct);

                // ---- ON-RECEIVE PAYMENTS (optional) ----
                if (onReceivePayments != null)
                {
                    foreach (var (method, amount, note) in onReceivePayments)
                    {
                        if (amount <= 0) continue;
                        var pay = NewPaymentRow(purchase, supplierId, method, amount, note, user, outletId);
                        db.PurchasePayments.Add(pay);
                    }

                    await PreflightValidateStagedPaymentsAsync(db, purchase, outletId, trace, ct);
                    // 08b.SAVE staged payments so the snapshot sees them
                    System.Diagnostics.Debug.WriteLine($"{trace} 08.StagePayments.SaveChanges");
                    await db.SaveChangesAsync(ct);

                    await EnsureNotOverpayAsync(db, purchase, "FinalizeReceive", ct);

                }


                global::System.Diagnostics.Debug.WriteLine($"{trace} 09.GL Payment Snapshot");
                await PostPaymentsDeltaAsync(db, purchase, user, ct);
                
                global::System.Diagnostics.Debug.WriteLine($"{trace} 10.Outbox.Upsert");
                await _outbox.EnqueueUpsertAsync(db, purchase, ct);

                global::System.Diagnostics.Debug.WriteLine($"{trace} 11.SaveChanges");
                await db.SaveChangesAsync(ct);

                global::System.Diagnostics.Debug.WriteLine($"{trace} 12.Commit");
                await tx.CommitAsync(ct);

                global::System.Diagnostics.Debug.WriteLine($"{trace} EXIT OK DocNo={purchase.DocNo}");
                return purchase;
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Debug.WriteLine($"{trace} EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    global::System.Diagnostics.Debug.WriteLine($"{trace} INNER: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                System.Diagnostics.Debug.WriteLine($"{trace} STACK: {ex.StackTrace}");
                try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                throw;
            }
        }

        private static decimal Round2(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

        private async Task EnsureNotOverpayAsync(
            PosClientDbContext db, Purchase p, string? when, CancellationToken ct)
        {
            // Sum all *effective* payments for this purchase after any pending inserts/edits
            var paid = await db.PurchasePayments
                .Where(x => x.PurchaseId == p.Id && x.IsEffective)
                .SumAsync(x => x.Amount, ct);

            var total = Round2(p.GrandTotal);
            var paid2 = Round2(paid);

            if (paid2 > total)
                throw new InvalidOperationException(
                    $"{when ?? "Validation"}: payments ({paid2:N2}) cannot exceed invoice total ({total:N2}).");
        }


        private async Task PreflightValidateStagedPaymentsAsync(
    PosClientDbContext db, Purchase p, int outletId, string trace, CancellationToken ct)
        {
            // Grab only NEW, unsaved PurchasePayment rows (staged in this txn)
            var staged = db.ChangeTracker.Entries<PurchasePayment>()
                .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added)
                .Select(e => e.Entity)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"{trace} [VALIDATE] StagedPayments.Count={staged.Count}");

            foreach (var pay in staged)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"{trace} [VALIDATE] Pay Id=0 PurchaseId={pay.PurchaseId} SupplierId={pay.SupplierId} OutletId={pay.OutletId} Method={pay.Method} BankAccountId={pay.BankAccountId} Amount={pay.Amount}");

                // 1) Purchase exists
                var hasPurchase = await db.Purchases.AsNoTracking().AnyAsync(x => x.Id == pay.PurchaseId, ct);
                if (!hasPurchase) throw new InvalidOperationException($"{trace} FK check failed: Purchase {pay.PurchaseId} not found.");

                // 2) Supplier/Party exists
                var hasParty = await db.Parties.AsNoTracking().AnyAsync(x => x.Id == pay.SupplierId, ct);
                if (!hasParty) throw new InvalidOperationException($"{trace} FK check failed: Supplier/Party {pay.SupplierId} not found.");

                // 3) Outlet exists (always required for payments; cash account belongs to an outlet)
                if (!(pay.OutletId is int oId) || oId == 0)
                    throw new InvalidOperationException($"{trace} FK check failed: Payment.OutletId is null/0.");
                var hasOutlet = await db.Outlets.AsNoTracking().AnyAsync(x => x.Id == oId, ct);
                if (!hasOutlet) throw new InvalidOperationException($"{trace} FK check failed: Outlet {oId} not found.");

                // 4) Bank method must have a valid bank account
                // 4) Bank method must have a valid, postable (leaf) bank account
                if (pay.Method == TenderMethod.Bank)
                {
                    if (!(pay.BankAccountId is int bId) || bId == 0)
                        throw new InvalidOperationException($"{trace} FK check failed: Bank payment missing BankAccountId.");

                    var bankMeta = await db.Accounts
                        .AsNoTracking()
                        .Where(a => a.Id == bId)
                        .Select(a => new { a.Id, a.AllowPosting, a.IsHeader, a.Type })
                        .FirstOrDefaultAsync(ct);

                    if (bankMeta is null)
                        throw new InvalidOperationException($"{trace} FK check failed: Bank account {bId} not found.");

                    if (!bankMeta.AllowPosting || bankMeta.IsHeader)
                        throw new InvalidOperationException($"{trace} Bank account {bId} is not a leaf/postable account.");

                    if (bankMeta.Type != AccountType.Asset)
                        throw new InvalidOperationException($"{trace} Bank account {bId} must be an Asset type.");
                }

            }
        }


        // Legacy ReceiveAsync kept as thin alias to FinalizeReceiveAsync (UI compatibility)
        public Task<Purchase> ReceiveAsync(
            Purchase model,
            IEnumerable<PurchaseLine> lines,
            string? user = null,
            CancellationToken ct = default)
            => FinalizeReceiveAsync(model, lines, Array.Empty<(TenderMethod, decimal, string?)>(),
                                    model.OutletId ?? 0, model.PartyId, null, null, user ?? "system", ct);

        // ------- Payments API (CASH or BANK only) -------

        public async Task<PurchasePayment> AddPaymentAsync(
    int purchaseId,
    PurchasePaymentKind kind,
    TenderMethod method,
    decimal amount,
    string? note,
    int outletId,
    int supplierId,
    string user,
    int? bankAccountId = null,
    CancellationToken ct = default)
        {
            if (amount <= 0m) throw new InvalidOperationException("Amount must be > 0.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);

            var p = await db.Purchases.FirstAsync(x => x.Id == purchaseId, ct);

            var pay = new PurchasePayment
            {
                PurchaseId = p.Id,
                SupplierId = supplierId,
                OutletId = outletId,
                WarehouseId = p.WarehouseId,
                Kind = kind,
                Method = method,
                Amount = Round2(amount),
                Note = note,
                BankAccountId = method == TenderMethod.Bank ? bankAccountId : null,
                IsEffective = true,
                TsUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = user
            };

            db.PurchasePayments.Add(pay);
            await db.SaveChangesAsync(ct);

            // Guard against overpay (includes the row we just saved)
            await EnsureNotOverpayAsync(db, p, "AddPayment", ct);

            // (Rebuild the GL payment snapshot using the same db)
            await PostPaymentsDeltaAsync(db, p, user, ct);

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, pay, ct);
            await tx.CommitAsync(ct);
            return pay;
        }


        public async Task UpdatePaymentAsync(
    int paymentId,
    decimal newAmount,
    TenderMethod newMethod,
    string? newNote,
    string user,
    CancellationToken ct = default)
        {
            if (newAmount <= 0m) throw new InvalidOperationException("Payment amount must be > 0.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);

            var pay = await db.PurchasePayments
                              .Include(x => x.Purchase)
                              .FirstOrDefaultAsync(x => x.Id == paymentId, ct)
                      ?? throw new InvalidOperationException("Payment not found.");

            // Apply edits
            pay.Amount = decimal.Round(newAmount, 2);
            pay.Method = newMethod;               // allow method change from UI
            pay.Note = newNote;
            pay.UpdatedAtUtc = DateTime.UtcNow;
            pay.UpdatedBy = user;

            // Guard: if switching to Bank, we must already have a BankAccountId on that payment
            if (newMethod == TenderMethod.Bank && !pay.BankAccountId.HasValue)
                throw new InvalidOperationException("Bank account is required when changing the payment method to Bank.");

            // Rebuild payment snapshot for this purchase (cash/bank grouped logic lives here)
            await PostPaymentsDeltaAsync(db, pay.Purchase!, user, ct);

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, pay, ct);
            await tx.CommitAsync(ct);
        }

        public async Task RemovePaymentAsync(int paymentId, string user, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);

            var pay = await db.PurchasePayments.Include(x => x.Purchase)
                                               .FirstOrDefaultAsync(x => x.Id == paymentId, ct)
                      ?? throw new InvalidOperationException("Payment not found.");

            pay.IsEffective = false;
            pay.UpdatedAtUtc = DateTime.UtcNow;
            pay.UpdatedBy = user;

            // Re-post delta for purchase chain
            await PostPaymentsDeltaAsync(db, pay.Purchase!, user, ct);

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, pay, ct);
            await tx.CommitAsync(ct);
        }

        public async Task<IReadOnlyList<PurchasePayment>> GetPaymentsAsync(int purchaseId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.PurchasePayments.AsNoTracking()
                        .Where(x => x.PurchaseId == purchaseId && x.IsEffective)
                        .OrderBy(x => x.TsUtc)
                        .ToListAsync(ct);
        }

        public async Task<bool> IsPurchaseBankConfiguredAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var id = await (from s in db.InvoiceSettingsLocals.AsNoTracking()
                            join c in db.Counters.AsNoTracking() on s.CounterId equals c.Id
                            where c.OutletId == outletId
                               && s.PurchaseBankAccountId != null
                               && s.PurchaseBankAccountId > 0
                            orderby s.UpdatedAtUtc descending
                            select s.PurchaseBankAccountId)
                           .FirstOrDefaultAsync(ct);

            return id.HasValue && id.Value > 0;
        }


        public async Task<List<Account>> ListBankAccountsForOutletAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Accounts.AsNoTracking()
                     .Where(a => a.AllowPosting && a.Type == AccountType.Asset && a.OutletId == null) // company-scope banks
                     .OrderBy(a => a.Name)
                     .ToListAsync(ct);
        }

        public async Task<int?> GetConfiguredPurchaseBankAccountIdAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // prefer most-recent counter in this outlet
            var id = await (from s in db.InvoiceSettingsLocals.AsNoTracking()
                            join c in db.Counters.AsNoTracking() on s.CounterId equals c.Id
                            where c.OutletId == outletId
                               && s.PurchaseBankAccountId != null
                               && s.PurchaseBankAccountId > 0
                            orderby s.UpdatedAtUtc descending
                            select s.PurchaseBankAccountId)
                           .FirstOrDefaultAsync(ct);

            if (id.HasValue && id.Value > 0) return id.Value;

            // fallback: any latest non-null config on this machine/db
            id = await db.InvoiceSettingsLocals.AsNoTracking()
                    .Where(s => s.PurchaseBankAccountId != null && s.PurchaseBankAccountId > 0)
                    .OrderByDescending(s => s.UpdatedAtUtc)
                    .Select(s => s.PurchaseBankAccountId)
                    .FirstOrDefaultAsync(ct);

            return id; // may be null
        }


        // ------- VOID -------

        public async Task VoidPurchaseAsync(int purchaseId, string reason, string? user = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var tx = await db.Database.BeginTransactionAsync(ct);

            var p = await db.Purchases
                .Include(x => x.Lines)
                .Include(x => x.Payments)
                .FirstOrDefaultAsync(x => x.Id == purchaseId, ct)
                ?? throw new InvalidOperationException("Purchase not found.");

            // Guard: cannot void original if any active returns exist
            var hasActiveReturns = await db.Purchases.AsNoTracking()
                .AnyAsync(r => r.IsReturn
                            && r.RefPurchaseId == p.Id
                            && r.Status != PurchaseStatus.Voided, ct);
            if (hasActiveReturns)
                throw new InvalidOperationException("This purchase has non-voided returns and cannot be voided. Void the returns first.");

            if (p.Status == PurchaseStatus.Voided) return;

            // Compute stock reversal deltas and guard negatives at destination
            var deltas = BuildVoidStockDeltas(p);
            await _stockGuard.EnsureNoNegativeAtLocationAsync(deltas, DateTime.UtcNow, ct);

            // Reverse stock ledger (entries for each item negative of original)
            await ApplyStockVoidAsync(db, p, DateTime.UtcNow, user ?? "system", ct);

            // Reverse GL chain for this purchase (gross + payments) by marking IsEffective=false
            await ((IGlPostingServiceDb)_gl).PostPurchaseVoidAsync(db, p, ct);
                        

            p.Status = PurchaseStatus.Voided;
            p.VoidReason = reason;
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user;

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, p, ct);
            await tx.CommitAsync(ct);
        }

        public Task VoidReturnAsync(int returnId, string reason, string? user = null, CancellationToken ct = default)
            => throw new NotImplementedException("Purchase Return void will be implemented in ReturnsService.");

        // -------------------------------
        // Internals
        // -------------------------------

        private static void NormalizeAndCompute(Purchase header, IEnumerable<PurchaseLine> lines)
        {
            var lineList = lines.ToList(); // local only; do NOT assign to header.Lines

            decimal sub = 0m;
            foreach (var l in lineList)
            {
                l.Qty = decimal.Round(l.Qty, 4);
                l.UnitCost = decimal.Round(l.UnitCost, 4);
                l.LineTotal = decimal.Round(l.Qty * l.UnitCost, 2);
                sub += l.LineTotal;
            }
            header.Subtotal = decimal.Round(sub, 2);
            header.GrandTotal = decimal.Round(header.Subtotal - header.Discount + header.Tax + header.OtherCharges, 2);
            header.CreditDue = decimal.Round(header.GrandTotal - header.CashPaid, 2);
        }

        // Pos.Persistence/Services/PurchasesService.cs
        private static void UpsertHeaderAndLines(PosClientDbContext db, Purchase header, IEnumerable<PurchaseLine> lines)
        {
            var nowUtc = DateTime.UtcNow;
            var user = header.UpdatedBy ?? header.CreatedBy ?? "system";

            // ensure the nav collection exists and is empty before we add
            if (header.Lines == null)
                header.Lines = new List<PurchaseLine>();
            else
                header.Lines.Clear();

            if (header.Id == 0)
            {
                header.CreatedAtUtc = nowUtc;
                header.CreatedBy = user;
                header.UpdatedAtUtc = nowUtc;
                header.UpdatedBy = user;

                db.Purchases.Add(header);
            }
            else
            {
                header.UpdatedAtUtc = nowUtc;
                header.UpdatedBy = user;

                db.Purchases.Update(header);

                // remove persisted lines for this header from DB
                var existing = db.PurchaseLines.Where(x => x.PurchaseId == header.Id);
                db.PurchaseLines.RemoveRange(existing);
            }

            foreach (var l in lines)
            {
                // treat incoming lines as fresh rows for this save
                l.Id = 0;
                l.PurchaseId = 0; // EF will set via nav after save
                l.CreatedAtUtc = l.CreatedAtUtc == default ? nowUtc : l.CreatedAtUtc;
                l.CreatedBy = string.IsNullOrWhiteSpace(l.CreatedBy) ? user : l.CreatedBy;
                l.UpdatedAtUtc = null;
                l.UpdatedBy = null;

                header.Lines.Add(l); // attach via nav so FK is populated safely
            }
        }



        private async Task<string> NextPurchaseNoAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var today = DateTime.UtcNow;
            var y = today.Year;
            var count = await db.Purchases.AsNoTracking().CountAsync(x =>
                x.OutletId == outletId && x.CreatedAtUtc.Year == y && x.Status != PurchaseStatus.Draft, ct);

            return $"PO-{y:0000}-{count + 1:00000}";
        }

        // In PurchasesService
        private IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> BuildPurchaseStockDeltas(PosClientDbContext db, Purchase p)
        {
            // Resolve destination (Outlet or Warehouse)
            (InventoryLocationType locType, int locId) = ResolveDestination(p);

            // Desired qty per item from current purchase lines
            var desiredByItem = p.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            // Already posted net qty per item for this purchase+location
            var postedByItem = db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "Purchase"
                          && se.RefId == p.Id
                          && se.LocationType == locType
                          && se.LocationId == locId)
                .GroupBy(se => se.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyChange));

            // 1) Items present in lines: delta = desired - posted
            foreach (var kv in desiredByItem)
            {
                var itemId = kv.Key;
                postedByItem.TryGetValue(itemId, out var posted);
                var delta = kv.Value - posted;
                if (delta != 0)
                {
                    // When yielding deltas:
                    yield return new StockDeltaDto(
                        ItemId: itemId,
                        OutletId: p.OutletId ?? 0,
                        LocType: locType,
                        LocId: locId,
                        Delta: delta
                    );
                }
            }

            // 2) Items that were previously posted but now removed from lines → delta = 0 - posted
            foreach (var kv in postedByItem)
            {
                if (desiredByItem.ContainsKey(kv.Key)) continue;
                var delta = 0 - kv.Value;
                if (delta != 0)
                {
                    yield return new StockDeltaDto(
                     ItemId: kv.Key,
                     OutletId: p.OutletId ?? 0,
                     LocType: locType,
                     LocId: locId,
                     Delta: delta
                 );
                }
            }
        }

        private async Task ApplyStockAsync(
    PosClientDbContext db,
    Purchase p,
    IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> deltas,
    Dictionary<int, decimal> unitCostByItem,
    DateTime nowUtc, string user, CancellationToken ct)
        {
            foreach (var d in deltas)
            {
                if (d.Delta == 0) continue;
                var cost = unitCostByItem.TryGetValue(d.ItemId, out var c) ? c : 0m;

                db.StockEntries.Add(new StockEntry
                {
                    OutletId = p.OutletId ?? 0,
                    ItemId = d.ItemId,
                    QtyChange = d.Delta,
                    UnitCost = cost,                  // <-- write cost
                    LocationType = d.LocType,
                    LocationId = d.LocId,
                    RefType = "Purchase",
                    RefId = p.Id,
                    Ts = nowUtc,
                    Note = p.DocNo,
                    CreatedAtUtc = nowUtc,
                    CreatedBy = user,
                    UpdatedAtUtc = null,
                    UpdatedBy = null
                });
            }
            await Task.CompletedTask;
        }



        // inside PurchasesService (private region)
        private static (InventoryLocationType locType, int locId) ResolveDestination(Purchase p)
        {
            // Prefer the explicit destination set on the Purchase header
            switch (p.LocationType)
            {
                case InventoryLocationType.Outlet:
                    if (p.OutletId is int oid && oid > 0) return (InventoryLocationType.Outlet, oid);
                    break;

                case InventoryLocationType.Warehouse:
                    if (p.WarehouseId is int wid && wid > 0) return (InventoryLocationType.Warehouse, wid);
                    break;
            }

            // Fallbacks (only if you really want them; otherwise throw):
            if (p.OutletId is int oid2 && oid2 > 0) return (InventoryLocationType.Outlet, oid2);
            if (p.WarehouseId is int wid2 && wid2 > 0) return (InventoryLocationType.Warehouse, wid2);

            throw new InvalidOperationException("Purchase destination is not set correctly (Outlet/Warehouse missing).");
        }


        private static IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> BuildVoidStockDeltas(Purchase p)
        {
            var locType = p.LocationType;
            int locId;
            if (locType == InventoryLocationType.Warehouse)
            {
                if (!(p.WarehouseId is int wid) || wid == 0)
                    throw new InvalidOperationException("WarehouseId is required for LocationType=Warehouse.");
                locId = wid;
            }
            else
            {
                if (!(p.OutletId is int oid) || oid == 0)
                    throw new InvalidOperationException("OutletId is required for LocationType=Outlet.");
                locId = oid;
            }

            foreach (var l in p.Lines)
            {
                yield return new Pos.Domain.Models.Inventory.StockDeltaDto(
                    ItemId: l.ItemId,
                    OutletId: p.OutletId ?? 0,
                    LocType: locType,
                    LocId: locId,
                    Delta: -l.Qty
                );
            }
        }



        private async Task ApplyStockVoidAsync(
    PosClientDbContext db, Purchase p,
    DateTime nowUtc, string user, CancellationToken ct)
        {
            var (lt, lid) = ResolveDestination(p);

            // Net currently posted for this purchase at this location
            var nets = await db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "Purchase"
                          && se.RefId == p.Id
                          && se.LocationType == lt
                          && se.LocationId == lid)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Net = g.Sum(x => x.QtyChange) })
                .ToListAsync(ct);

            foreach (var r in nets)
            {
                if (r.Net == 0) continue;
                db.StockEntries.Add(new StockEntry
                {
                    OutletId = p.OutletId ?? 0,
                    ItemId = r.ItemId,
                    QtyChange = -r.Net,          // full reversal to zero the purchase’s net
                    UnitCost = 0m,
                    LocationType = lt,
                    LocationId = lid,
                    RefType = "PurchaseVoid",
                    RefId = p.Id,
                    Ts = nowUtc,
                    Note = $"VOID {p.DocNo}",
                            // ✅ audit
                    CreatedAtUtc = nowUtc,
                    CreatedBy = user,
                    UpdatedAtUtc = null,
                    UpdatedBy = null
                });
            }
            await Task.CompletedTask;
        }


        private PurchasePayment NewPaymentRow(
    Purchase p,
    int supplierId,
    TenderMethod method,
    decimal amount,
    string? note,
    string user,
    int outletIdForCash)   // ✅ explicit outlet for payment (cash/bank)
        {
            return new PurchasePayment
            {
                PurchaseId = p.Id,
                SupplierId = supplierId,
                OutletId = outletIdForCash,   // ✅ always set
                WarehouseId = p.WarehouseId,  // ok if null
                Kind = PurchasePaymentKind.OnReceive,
                Method = method,
                Amount = decimal.Round(amount, 2),
                Note = note,
                IsEffective = true,
                TsUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = user,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = user
            };
        }


        private async Task<int> RequireDefaultBankAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var id = await (from s in db.InvoiceSettingsLocals.AsNoTracking()
                            join c in db.Counters.AsNoTracking() on s.CounterId equals c.Id
                            where c.OutletId == outletId
                               && s.PurchaseBankAccountId != null
                               && s.PurchaseBankAccountId > 0
                            orderby s.UpdatedAtUtc descending
                            select s.PurchaseBankAccountId)
                           .FirstOrDefaultAsync(ct);

            if (!id.HasValue || id.Value <= 0)
                throw new InvalidOperationException("No default Purchase Bank account configured in Invoice Settings (counter-scoped).");

            return id.Value;
        }


        /// <summary>
        /// Computes delta across all effective payments and posts two rows per payment delta:
        /// Supplier (DR) vs Cash-in-hand (CR) OR Bank (CR).
        /// </summary>
        private async Task PostPaymentsDeltaAsync(PosClientDbContext db, Purchase p, string? user, CancellationToken ct)
        {
            // Snapshot payments
            var pays = await db.PurchasePayments
                .Where(x => x.PurchaseId == p.Id && x.IsEffective)
                .ToListAsync(ct);

            var cashTotal = pays.Where(x => x.Method == TenderMethod.Cash).Sum(x => x.Amount);
            var bankGroups = pays.Where(x => x.Method == TenderMethod.Bank)
                                 .GroupBy(x => x.BankAccountId)
                                 .Where(g => g.Key.HasValue);

            // Mirror header
            var bankTotal = bankGroups.Sum(g => g.Sum(x => x.Amount));
            p.CashPaid = cashTotal;
            p.CreditDue = decimal.Round(p.GrandTotal - p.CashPaid - bankTotal, 2);
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user;

            // Inactivate previous PAYMENT snapshot
            await db.GlEntries
            .Where(e =>
                e.IsEffective &&
                e.DocType == GlDocType.Purchase &&
                e.DocSubType == GlDocSubType.Purchase_Payment &&
                (
                    e.ChainId == p.PublicId ||
                    e.DocId == p.Id
                ))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEffective, false), ct);


            var tsUtc = DateTime.UtcNow;
            var eff = tsUtc;
            var outletId = p.OutletId;

            // Supplier account (must exist)
            var supplierAccId = await db.Parties.AsNoTracking()
                .Where(x => x.Id == p.PartyId)
                .Select(x => x.AccountId)
                .FirstOrDefaultAsync(ct);

            if (!supplierAccId.HasValue)
                throw new InvalidOperationException("Supplier Party.AccountId is missing. Link the supplier to a ledger account before posting payments.");

            // Resolve Cash-in-Hand (must exist for this outlet)
            int cashAccId = 0;
            if (cashTotal > 0m)
            {
                // ✅ your COA service that guarantees the right outlet cash-in-hand
                cashAccId = await _coa.GetCashAccountIdAsync(outletId!.Value, ct);
                if (cashAccId == 0)
                    throw new InvalidOperationException($"Cash-in-Hand account for outlet {outletId} not found.");
            }


            // DEBUG: log resolved ids so we can see the exact numbers
            System.Diagnostics.Debug.WriteLine($"[PaymentsSnapshot] SupplierAccId={supplierAccId} CashAccId={cashAccId} OutletId={outletId} CashTotal={cashTotal} BankGroups={bankGroups.Count()}");

            // Cash rows
            if (cashTotal > 0m)
            {
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = tsUtc,
                    EffectiveDate = eff,
                    OutletId = outletId,
                    AccountId = supplierAccId.Value,
                    Debit = cashTotal,
                    Credit = 0m,
                    DocType = GlDocType.Purchase,
                    DocSubType = GlDocSubType.Purchase_Payment,
                    DocId = p.Id,
                    DocNo = p.DocNo,
                    ChainId = p.PublicId,
                    IsEffective = true,
                    PartyId = p.PartyId,
                    Memo = "Payment (Cash)"
                });
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = tsUtc,
                    EffectiveDate = eff,
                    OutletId = outletId,
                    AccountId = cashAccId,
                    Debit = 0m,
                    Credit = cashTotal,
                    DocType = GlDocType.Purchase,
                    DocSubType = GlDocSubType.Purchase_Payment,
                    DocId = p.Id,
                    DocNo = p.DocNo,
                    ChainId = p.PublicId,
                    IsEffective = true,
                    PartyId = p.PartyId,
                    Memo = "Payment (Cash)"
                });
            }

            // Bank rows (per bank account id)
            foreach (var g in bankGroups)
            {
                var total = g.Sum(x => x.Amount);
                if (total <= 0m) continue;

                var bankAccId = g.Key!.Value;

                // Validate the bank account exists (FK safety)
                var exists = await db.Accounts.AsNoTracking().AnyAsync(a => a.Id == bankAccId, ct);
                if (!exists) throw new InvalidOperationException($"Selected bank account (Id={bankAccId}) not found.");

                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = tsUtc,
                    EffectiveDate = eff,
                    OutletId = outletId,
                    AccountId = supplierAccId.Value,
                    Debit = total,
                    Credit = 0m,
                    DocType = GlDocType.Purchase,
                    DocSubType = GlDocSubType.Purchase_Payment,
                    DocId = p.Id,
                    DocNo = p.DocNo,
                    ChainId = p.PublicId,
                    IsEffective = true,
                    PartyId = p.PartyId,
                    Memo = "Payment (Bank)"
                });
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = tsUtc,
                    EffectiveDate = eff,
                    OutletId = outletId,
                    AccountId = bankAccId,
                    Debit = 0m,
                    Credit = total,
                    DocType = GlDocType.Purchase,
                    DocSubType = GlDocSubType.Purchase_Payment,
                    DocId = p.Id,
                    DocNo = p.DocNo,
                    ChainId = p.PublicId,
                    IsEffective = true,
                    PartyId = p.PartyId,
                    Memo = "Payment (Bank)"
                });
            }
        }



        /// <summary>
        /// Auto-pick last UnitCost/Discount/TaxRate from latest FINAL purchase line of this item.
        /// </summary>
        public async Task<(decimal unitCost, decimal discount, decimal taxRate)?> GetLastPurchaseDefaultsAsync(int itemId, CancellationToken ct = default)
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

        public async Task<Purchase?> LoadDraftWithLinesAsync(int id, CancellationToken ct = default)
        {
            await using var _db = await _dbf.CreateDbContextAsync(ct);
            return await _db.Purchases
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == PurchaseStatus.Draft, ct);
        }

        public async Task<Purchase> LoadWithLinesAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Purchases
                .Include(p => p.Lines)
                .Include(p => p.Party)
                .FirstAsync(p => p.Id == id, ct);
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

        // Build per-item effective unit cost for this purchase header.
        // Rule: start from line UnitCost, allocate header-level Discount (-) and OtherCharges (+)
        // proportionally to each line's pre-VAT amount. Tax is excluded from capitalized cost.
        private static Dictionary<int, decimal> BuildPerItemUnitCostMap(Purchase p)
        {
            // group by item: base amount = sum(Qty * UnitCost)
            var groups = p.Lines
                .GroupBy(l => l.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Qty = g.Sum(x => x.Qty),
                    BaseAmount = g.Sum(x => x.UnitCost * x.Qty)
                })
                .ToList();

            var baseTotal = groups.Sum(x => x.BaseAmount);
            var allocTotal = (-p.Discount) + p.OtherCharges; // discount reduces, other charges add

            var map = new Dictionary<int, decimal>(groups.Count);
            foreach (var g in groups)
            {
                if (g.Qty == 0m) { map[g.ItemId] = 0m; continue; }

                var share = baseTotal > 0m ? (g.BaseAmount / baseTotal) : 0m;
                var effAmount = g.BaseAmount + (allocTotal * share);
                var effUnit = Math.Round(effAmount / g.Qty, 4);
                map[g.ItemId] = effUnit;
            }
            return map;
        }

    }
}
