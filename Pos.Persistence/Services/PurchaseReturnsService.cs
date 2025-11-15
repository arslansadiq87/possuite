// Pos.Persistence/Services/PurchaseReturnsService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Models.Purchases;
using Pos.Domain.Services;
using Pos.Persistence.Sync;   // for EnqueueUpsertAsync extensions
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;


// Dump EF model

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Clean implementation for Purchase Returns (separate from PurchasesService).
    /// Covers:
    ///  - Return WITHOUT invoice
    ///  - Return WITH invoice
    ///  - Amend return
    ///  - Void return
    /// </summary>
    public sealed class PurchaseReturnsService : IPurchaseReturnsService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;
        private readonly IInventoryReadService _invRead;
        private readonly IGlPostingServiceDb _gl;

        public PurchaseReturnsService(
            IDbContextFactory<PosClientDbContext> dbf,
            IOutboxWriter outbox,
            IInventoryReadService invRead,
            IGlPostingServiceDb gl)
        {
            _dbf = dbf;
            _outbox = outbox;
            _invRead = invRead;
            _gl = gl;
        }
        private static void LogHeader(Purchase p, string prefix)
        {
            Debug.WriteLine(
                $"{prefix} Id={p.Id} PartyId={p.PartyId} IsReturn={p.IsReturn} RefPurchaseId={p.RefPurchaseId} " +
                $"LocType={p.LocationType} OutletId={p.OutletId} WarehouseId={p.WarehouseId} " +
                $"Subtotal={p.Subtotal} GrandTotal={p.GrandTotal} CashPaid={p.CashPaid} CreditDue={p.CreditDue}");
        }


        // ----------------------------------------------------
        //  WITH INVOICE: build draft (max allowed qty per line)
        // ----------------------------------------------------
        public async Task<PurchaseReturnDraft> BuildReturnDraftFromInvoiceAsync(
            int originalPurchaseId,
            CancellationToken ct = default)
        {
            var trace = $"[ReturnSvc.BuildDraft] {Guid.NewGuid():N}";
            Debug.WriteLine($"{trace} ENTER originalPurchaseId={originalPurchaseId}");
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // 1) Load FINAL purchase with lines + items
            var p = await db.Purchases
                .Include(x => x.Lines).ThenInclude(l => l.Item)
                .AsNoTracking()
                .FirstAsync(x => x.Id == originalPurchaseId
                                 && x.Status == PurchaseStatus.Final
                                 && !x.IsReturn,
                    ct);
            Debug.WriteLine($"{trace} BasePurchase Id={p.Id} PartyId={p.PartyId} Lines={p.Lines.Count}");

            // 2) Compute already returned per original line (only non-voided returns)
            var alreadyReturned = await db.Purchases
                .Where(r => r.IsReturn
                            && r.RefPurchaseId == originalPurchaseId
                            && r.Status != PurchaseStatus.Voided)
                .SelectMany(r => r.Lines)
                .GroupBy(l => l.RefPurchaseLineId)
                .Select(g => new
                {
                    OriginalLineId = g.Key,
                    ReturnedAbs = Math.Abs(g.Sum(z => z.Qty))
                })
                .ToListAsync(ct);

            var returnedByLine = alreadyReturned
                .Where(x => x.OriginalLineId.HasValue)
                .ToDictionary(x => x.OriginalLineId!.Value, x => x.ReturnedAbs);

            // 3) Build draft lines with remaining allowed Qty
            var draft = new PurchaseReturnDraft
            {
                PartyId = p.PartyId,
                LocationType = p.LocationType,
                OutletId = p.OutletId,
                WarehouseId = p.WarehouseId,
                RefPurchaseId = p.Id
            };

            foreach (var line in p.Lines)
            {
                var purchased = Math.Abs(line.Qty);
                returnedByLine.TryGetValue(line.Id, out var already);
                var remain = purchased - already;
                if (remain <= 0) continue;

                draft.Lines.Add(new PurchaseReturnDraftLine
                {
                    OriginalLineId = line.Id,
                    ItemId = line.ItemId,
                    ItemName = line.Item?.Name ?? "",
                    UnitCost = line.UnitCost,
                    MaxReturnQty = remain,
                    ReturnQty = remain
                });
            }
            Debug.WriteLine($"{trace} DRAFT.Lines={draft.Lines.Count} PartyId={draft.PartyId} LocType={draft.LocationType} OutletId={draft.OutletId} WarehouseId={draft.WarehouseId}");
            Debug.WriteLine($"{trace} EXIT");
            return draft;
        }

        // ----------------------------------------------------
        //  WITH INVOICE: finalize return
        // ----------------------------------------------------
        public async Task<Purchase> FinalizeReturnFromInvoiceAsync(
    Purchase header,
    IEnumerable<PurchaseReturnDraftLine> draftLines,
    IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
    string user,
    CancellationToken ct = default)
        {
            var trace = $"[ReturnSvc.FromInvoice] {Guid.NewGuid():N}";
            Debug.WriteLine($"{trace} ENTER user={user} header.Id={header.Id} refPurchaseId={header.RefPurchaseId}");
            LogHeader(header, $"{trace} HEADER(pre)");

            if (!header.IsReturn)
                throw new InvalidOperationException("FinalizeReturnFromInvoiceAsync requires IsReturn=true.");
            if (header.RefPurchaseId is null or <= 0)
                throw new InvalidOperationException("RefPurchaseId is required for 'Return with invoice'.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var nowUtc = DateTime.UtcNow;

            // (optional) model FK wiring dump once per call
            DumpModelFks(db, trace);

            // 1) Load original purchase (for caps)
            var original = await db.Purchases.Include(x => x.Lines)
                .FirstAsync(x => x.Id == header.RefPurchaseId.Value, ct);
            Debug.WriteLine($"{trace} OriginalPurchase Id={original.Id} Lines={original.Lines.Count}");

            // 2) Build lines from draft with invoice caps
            var lineList = BuildReturnLinesFromDraft(header, original, draftLines);
            Debug.WriteLine($"{trace} LinesBuilt count={lineList.Count} SumQty={lineList.Sum(x => x.Qty)}");

            // 3) On-hand guard
            await EnforceOnHandCapsAsync(db, header, lineList, ct);
            Debug.WriteLine($"{trace} OnHandGuard OK");

            // 4) Totals
            NormalizeAndComputeReturn(header, lineList, refunds);
            LogHeader(header, $"{trace} HEADER(norm)");

            // 5) Payments
            ApplyRefundsToPayments(header, refunds, user, nowUtc);
            GuardBankRefundPresence(header);
            await ValidateBankRefundAccountsAsync(db, header, ct);
            EnsureBankPaymentsHaveAccount(header);
            Debug.WriteLine($"{trace} Payments count={header.Payments?.Count ?? 0}");

            // 6) Persist header + lines (attach)
            UpsertReturnHeaderAndLines(db, header, lineList, nowUtc, user);
            Debug.WriteLine($"{trace} UpsertHeaderAndLines done (isNew={(header.Id == 0)})");

            // ---- SAVE #1: get real Purchase.Id for StockEntries.RefId
            DumpTrackedFkValuesSafe(db, trace);
            DumpChangeTracker(db, $"{trace}.BEFORE_SAVE_PHASE1");
            await db.SaveChangesAsync(ct);
            Debug.WriteLine($"{trace} Header+Lines saved (Id={header.Id})");

            // ---- STOCK OUT for return
            var stockDeltas = BuildReturnStockDeltas(db, header).ToList();
            Debug.WriteLine($"{trace} StockDeltas count={stockDeltas.Count} sum={stockDeltas.Sum(d => d.Delta)}");
            await ApplyReturnStockAsync(db, header, stockDeltas, nowUtc, user, ct);
            Debug.WriteLine($"{trace} StockEntries applied");

            // ---- GL (gross + refunds)
            await _gl.PostPurchaseReturnAsync(db, header, ct);
            Debug.WriteLine($"{trace} GL.PostPurchaseReturnAsync OK");

            // ---- Outbox (header)
            await _outbox.EnqueueUpsertAsync(db, header, ct);

            // ---- SAVE #2: stock + GL + outbox
            DumpChangeTracker(db, $"{trace}.BEFORE_SAVE_PHASE2");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            Debug.WriteLine($"{trace} PHASE2 SAVE OK -> EXIT Id={header.Id} DocNo={header.DocNo}");
            return header;
        }

        private static void EnsureBankPaymentsHaveAccount(Purchase p)
        {
            if (p.Payments == null) return;
            foreach (var pay in p.Payments)
            {
                if (pay.Method == TenderMethod.Bank && !pay.BankAccountId.HasValue)
                    throw new InvalidOperationException("Bank refund selected but no bank account was chosen.");
            }
        }

        private async Task PreSaveFkChecksAndDumpAsync(PosClientDbContext db, Purchase header, List<PurchaseLine> lines, string trace, CancellationToken ct)
        {
            Debug.WriteLine($"{trace} PRE-SAVE header: Id={header.Id} PartyId={header.PartyId} RefPurchaseId={header.RefPurchaseId} LocType={header.LocationType} OutletId={header.OutletId} WarehouseId={header.WarehouseId} Subtotal={header.Subtotal} GrandTotal={header.GrandTotal} CashPaid={header.CashPaid} CreditDue={header.CreditDue}");

            var partyOk = await db.Parties.AnyAsync(x => x.Id == header.PartyId, ct);
            var outletOk = header.OutletId is null || await db.Outlets.AnyAsync(x => x.Id == header.OutletId, ct);
            var whOk = header.WarehouseId is null || await db.Warehouses.AnyAsync(x => x.Id == header.WarehouseId, ct);
            var refOk = header.RefPurchaseId is null || await db.Purchases.AnyAsync(x => x.Id == header.RefPurchaseId, ct);

            Debug.WriteLine($"{trace} FK PARTY={partyOk} OUTLET={outletOk} WAREHOUSE={whOk} REF_PURCHASE={refOk}");

            foreach (var ln in lines)
            {
                var itemOk = await db.Items.AnyAsync(x => x.Id == ln.ItemId, ct);
                Debug.WriteLine($"{trace} LINE item={ln.ItemId} qty={ln.Qty} unitCost={ln.UnitCost} itemFK={itemOk}");
            }

            if (header.Payments != null)
            {
                foreach (var pay in header.Payments)
                {
                    bool? bankOk = null;
                    if (pay.Method == TenderMethod.Bank && pay.BankAccountId.HasValue)
                        bankOk = await db.Accounts.AnyAsync(a => a.Id == pay.BankAccountId.Value, ct);
                    Debug.WriteLine($"{trace} PAY method={pay.Method} amount={pay.Amount} bankId={pay.BankAccountId} bankFK={bankOk?.ToString() ?? "<n/a>"} note='{pay.Note}'");
                }
            }

            DumpChangeTracker(db, $"{trace}.BEFORE_SAVE_PHASE1");
        }

        // ----------------------------------------------------
        //  WITHOUT INVOICE: finalize free-form return
        // ----------------------------------------------------
        public async Task<Purchase> FinalizeReturnWithoutInvoiceAsync(
    Purchase header,
    IEnumerable<PurchaseLine> lines,
    IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
    string user,
    CancellationToken ct = default)
        {
            var trace = $"[ReturnSvc.WithoutInvoice] {Guid.NewGuid():N}";
            Debug.WriteLine($"{trace} ENTER user={user} header.Id={header.Id}");
            LogHeader(header, $"{trace} HEADER(pre)");

            if (!header.IsReturn)
                throw new InvalidOperationException("FinalizeReturnWithoutInvoiceAsync requires IsReturn=true.");
            if (header.RefPurchaseId is not null && header.RefPurchaseId > 0)
                throw new InvalidOperationException("Free-form returns cannot have RefPurchaseId.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var nowUtc = DateTime.UtcNow;

            var lineList = lines.ToList();
            if (lineList.Count == 0)
                throw new InvalidOperationException("Return must contain at least one line.");

            // Ensure negatives (stock out)
            foreach (var l in lineList)
                if (l.Qty > 0) l.Qty = -l.Qty;
            Debug.WriteLine($"{trace} NormalizedLines SumQty={lineList.Sum(x => x.Qty)}");

            await EnforceOnHandCapsAsync(db, header, lineList, ct);
            Debug.WriteLine($"{trace} OnHandGuard OK");

            NormalizeAndComputeReturn(header, lineList, refunds);
            LogHeader(header, $"{trace} HEADER(norm)");

            ApplyRefundsToPayments(header, refunds, user, nowUtc);
            GuardBankRefundPresence(header);
            await ValidateBankRefundAccountsAsync(db, header, ct);
            EnsureBankPaymentsHaveAccount(header);
            Debug.WriteLine($"{trace} Payments count={header.Payments?.Count ?? 0}");

            UpsertReturnHeaderAndLines(db, header, lineList, nowUtc, user);
            Debug.WriteLine($"{trace} UpsertReturnHeaderAndLines done (isNew={(header.Id == 0)})");

            // ---- SAVE #1: get real Purchase.Id for StockEntries.RefId
            DumpTrackedFkValuesSafe(db, trace);
            DumpChangeTracker(db, $"{trace}.BEFORE_SAVE_PHASE1");
            await db.SaveChangesAsync(ct);
            Debug.WriteLine($"{trace} Header+Lines saved (Id={header.Id})");

            // ---- STOCK OUT for return
            var stockDeltas = BuildReturnStockDeltas(db, header).ToList();
            Debug.WriteLine($"{trace} StockDeltas count={stockDeltas.Count} sum={stockDeltas.Sum(d => d.Delta)}");
            await ApplyReturnStockAsync(db, header, stockDeltas, nowUtc, user, ct);
            Debug.WriteLine($"{trace} StockEntries applied");

            // ---- GL (gross + refunds)
            await _gl.PostPurchaseReturnAsync(db, header, ct);
            Debug.WriteLine($"{trace} GL.PostPurchaseReturnAsync OK");

            // ---- Outbox (header)
            await _outbox.EnqueueUpsertAsync(db, header, ct);

            // ---- SAVE #2: stock + GL + outbox
            DumpChangeTracker(db, $"{trace}.BEFORE_SAVE_PHASE2");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            Debug.WriteLine($"{trace} PHASE2 SAVE OK -> EXIT Id={header.Id} DocNo={header.DocNo}");
            return header;
        }


        // ----------------------------------------------------
        //  AMEND RETURN
        // ----------------------------------------------------
        public async Task<(Purchase Header, List<PurchaseLine> Lines)> LoadReturnForAmendAsync(
            int returnId,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var p = await db.Purchases
                .Include(x => x.Lines)
                .Include(x => x.Payments)     // <-- add this
                .FirstAsync(x => x.Id == returnId && x.IsReturn, ct);

            var lines = p.Lines
                .OrderBy(x => x.Id)
                .ToList();

            return (p, lines);
        }

        public async Task<Purchase> FinalizeReturnAmendAsync(
            Purchase header,
            IEnumerable<PurchaseLine> newLines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
            string user,
            CancellationToken ct = default)
        {
            var trace = $"[ReturnSvc.Amend] {Guid.NewGuid():N}";
            Debug.WriteLine($"{trace} ENTER user={user} header.Id={header.Id}");
            LogHeader(header, $"{trace} HEADER(pre)");

            if (!header.IsReturn)
                throw new InvalidOperationException("FinalizeReturnAmendAsync requires IsReturn=true.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var nowUtc = DateTime.UtcNow;

            var existing = await db.Purchases
                .Include(x => x.Lines)
                .Include(x => x.Payments)
                .FirstAsync(x => x.Id == header.Id && x.IsReturn, ct);

            Debug.WriteLine($"{trace} Existing.Id={existing.Id} Lines={existing.Lines.Count} Payments={existing.Payments.Count}");

            var oldLines = existing.Lines.ToList();
            var newLineList = newLines.ToList();
            Debug.WriteLine($"{trace} NewLines count={newLineList.Count} SumQty={newLineList.Sum(x => x.Qty)}");

            // Enforce on-hand (using delta old→new)
            await EnforceOnHandCapsForAmendAsync(db, existing, oldLines, newLineList, ct);
            Debug.WriteLine($"{trace} OnHandGuardAmend OK");

            // Compute totals based on new lines (using header as transient calculator)
            NormalizeAndComputeReturn(header, newLineList, refunds);
            LogHeader(header, $"{trace} HEADER(norm)");

            // Replace lines
            existing.Lines.Clear();
            foreach (var l in newLineList)
            {
                l.Id = 0;
                l.PurchaseId = existing.Id;
                l.Purchase = existing;
                existing.Lines.Add(l);
            }

            Debug.WriteLine($"{trace} LinesReplaced count={existing.Lines.Count}");

            // Rebuild payments from refunds
            ApplyRefundsToPayments(existing, refunds, user, nowUtc);
            Debug.WriteLine($"{trace} PaymentsRebuilt count={existing.Payments.Count}");

            // Copy totals to existing
            existing.Subtotal = header.Subtotal;
            existing.Discount = header.Discount;
            existing.Tax = header.Tax;
            existing.OtherCharges = header.OtherCharges;
            existing.GrandTotal = header.GrandTotal;
            existing.CashPaid = header.CashPaid;
            existing.CreditDue = header.CreditDue;

            existing.UpdatedAtUtc = nowUtc;
            existing.UpdatedBy = user;
            existing.Revision += 1;

            // ▶▶ ADD THIS: recompute and apply stock deltas for the amendment
            var stockDeltas = BuildReturnStockDeltas(db, existing).ToList();
            Debug.WriteLine($"{trace} StockDeltas(AMEND) count={stockDeltas.Count} sum={stockDeltas.Sum(d => d.Delta)}");
            await ApplyReturnStockAsync(db, existing, stockDeltas, nowUtc, user, ct);
            Debug.WriteLine($"{trace} StockEntries(AMEND) applied");

            // Re-post full GL snapshot for this return (invalidates old rows for this chain)
            await _gl.PostPurchaseReturnAsync(db, existing, ct);
            Debug.WriteLine($"{trace} GL.PostPurchaseReturnAsync OK");

            await _outbox.EnqueueUpsertAsync(db, existing, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            Debug.WriteLine($"{trace} EXIT Id={existing.Id} Rev={existing.Revision}");

            return existing;
        }

        // ===== RETURN DOC NO =====
        private async Task<string> NextReturnNoAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var y = DateTime.UtcNow.Year;
            // Count only non-drafts that are returns from this outlet and this year
            var count = await db.Purchases.AsNoTracking().CountAsync(x =>
                x.IsReturn == true &&
                x.OutletId == outletId &&
                x.CreatedAtUtc.Year == y &&
                x.Status != PurchaseStatus.Draft, ct);

            // Prefix for Purchase Return
            return $"PR-{y:0000}-{count + 1:00000}";
        }

        // ===== STOCK DELTAS (RETURN) =====
        // Build deltas like PurchasesService.BuildPurchaseStockDeltas but for "PurchaseReturn" (negative qty)
        private IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> BuildReturnStockDeltas(PosClientDbContext db, Purchase p)
        {
            // destination for a return is the SOURCE we are returning from
            var locType = p.LocationType;
            var locId = (locType == InventoryLocationType.Warehouse) ? (p.WarehouseId ?? 0) : (p.OutletId ?? 0);

            // desired (what lines say) — lines are already negative for returns
            var desiredByItem = p.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            // already posted for this return doc (id) and location
            var postedByItem = db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "PurchaseReturn"
                          && se.RefId == p.Id
                          && se.LocationType == locType
                          && se.LocationId == locId)
                .GroupBy(se => se.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyChange));

            // items present in lines
            foreach (var kv in desiredByItem)
            {
                postedByItem.TryGetValue(kv.Key, out var posted);
                var delta = kv.Value - posted;
                if (delta != 0)
                    yield return new Pos.Domain.Models.Inventory.StockDeltaDto(
                        ItemId: kv.Key,
                        OutletId: p.OutletId ?? 0,
                        LocType: locType,
                        LocId: locId,
                        Delta: delta
                    );
            }

            // items that were previously posted but now removed
            foreach (var kv in postedByItem)
            {
                if (desiredByItem.ContainsKey(kv.Key)) continue;
                var delta = 0 - kv.Value;
                if (delta != 0)
                    yield return new Pos.Domain.Models.Inventory.StockDeltaDto(
                        ItemId: kv.Key,
                        OutletId: p.OutletId ?? 0,
                        LocType: locType,
                        LocId: locId,
                        Delta: delta
                    );
            }
        }

        // ===== APPLY STOCK (RETURN) =====
        // Same shape as PurchasesService.ApplyStockAsync but RefType = "PurchaseReturn"
        private async Task ApplyReturnStockAsync(
            PosClientDbContext db, Purchase p,
            IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> deltas,
            DateTime nowUtc, string user, CancellationToken ct)
        {
            foreach (var d in deltas)
            {
                if (d.Delta == 0) continue;

                db.StockEntries.Add(new StockEntry
                {
                    OutletId = p.OutletId ?? 0,
                    ItemId = d.ItemId,
                    QtyChange = d.Delta, // will be negative for stock-out
                    UnitCost = 0m,
                    LocationType = d.LocType,
                    LocationId = d.LocId,
                    RefType = "PurchaseReturn",
                    RefId = p.Id,        // requires header.Id to be assigned (save header first)
                    Ts = nowUtc,
                    Note = p.DocNo,
                    // ✅ audit
                    CreatedAtUtc = nowUtc,
                    CreatedBy = user,
                    UpdatedAtUtc = null,
                    UpdatedBy = null
                });
            }
            await Task.CompletedTask;
        }

        // ----------------------------------------------------
        //  VOID RETURN
        // ----------------------------------------------------
        public async Task VoidReturnAsync(
    int returnId,
    string reason,
    string? user = null,
    CancellationToken ct = default)
        {
            var trace = $"[ReturnSvc.Void] {Guid.NewGuid():N}";
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var p = await db.Purchases
                .Include(x => x.Lines)
                .FirstAsync(x => x.Id == returnId && x.IsReturn, ct);

            if (p.Status == PurchaseStatus.Voided)
                return;

            p.Status = PurchaseStatus.Voided;
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = user ?? "system";
            p.VoidReason = reason;

            // ▶ 1) Reverse stock for this return
            var nowUtc = DateTime.UtcNow;
            var reversals = BuildReturnVoidReversalDeltas(db, p).ToList();
            await ApplyReturnVoidStockAsync(db, p, reversals, nowUtc, user ?? "system", ct);

            // 2) GL: invalidate gross + refunds for this chain (cash/AP)
            await _gl.PostPurchaseReturnVoidAsync(db, p, ct);

            // 3) Outbox
            await _outbox.EnqueueUpsertAsync(db, p, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }


        private IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> BuildReturnVoidReversalDeltas(PosClientDbContext db, Purchase p)
        {
            // Sum everything already posted for this return, then emit inverse deltas
            var locType = p.LocationType;
            var locId = (locType == InventoryLocationType.Warehouse) ? (p.WarehouseId ?? 0) : (p.OutletId ?? 0);

            var postedByItem = db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "PurchaseReturn"
                             && se.RefId == p.Id
                             && se.LocationType == locType
                             && se.LocationId == locId)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Sum = g.Sum(x => x.QtyChange) })
                .ToList();

            foreach (var row in postedByItem)
            {
                var delta = -row.Sum; // full reversal
                if (delta == 0) continue;
                yield return new Pos.Domain.Models.Inventory.StockDeltaDto(
                    ItemId: row.ItemId,
                    OutletId: p.OutletId ?? 0,
                    LocType: locType,
                    LocId: locId,
                    Delta: delta
                );
            }
        }

        private async Task ApplyReturnVoidStockAsync(
    PosClientDbContext db, Purchase p,
    IEnumerable<Pos.Domain.Models.Inventory.StockDeltaDto> deltas,
    DateTime nowUtc, string user, CancellationToken ct)
        {
            foreach (var d in deltas)
            {
                if (d.Delta == 0) continue;
                db.StockEntries.Add(new StockEntry
                {
                    OutletId = p.OutletId ?? 0,
                    ItemId = d.ItemId,
                    QtyChange = d.Delta,            // typically positive to put stock back
                    UnitCost = 0m,
                    LocationType = d.LocType,
                    LocationId = d.LocId,
                    RefType = "PurchaseReturn",     // keep same ref type, tag in Note
                    RefId = p.Id,
                    Ts = nowUtc,
                    Note = (string.IsNullOrWhiteSpace(p.DocNo) ? p.Id.ToString() : p.DocNo) + " · VOID",
                    CreatedAtUtc = nowUtc,
                    CreatedBy = user ?? "system",
                    UpdatedAtUtc = null,
                    UpdatedBy = null
                });
            }
            await Task.CompletedTask;
        }

        // ----------------------------------------------------
        //  PRIVATE HELPERS
        // ----------------------------------------------------

        private static List<PurchaseLine> BuildReturnLinesFromDraft(
            Purchase header,
            Purchase original,
            IEnumerable<PurchaseReturnDraftLine> draftLines)
        {
            var byId = original.Lines.ToDictionary(x => x.Id);

            var result = new List<PurchaseLine>();

            foreach (var dl in draftLines)
            {
                if (dl.ReturnQty <= 0) continue;

                if (dl.OriginalLineId is null || !byId.TryGetValue(dl.OriginalLineId.Value, out var src))
                    throw new InvalidOperationException("Draft line missing valid OriginalLineId.");

                var max = dl.MaxReturnQty;
                var qty = Math.Min(dl.ReturnQty, max);

                var line = new PurchaseLine
                {
                    Purchase = header,
                    ItemId = src.ItemId,
                    Qty = -Math.Abs(qty),          // stock OUT
                    UnitCost = src.UnitCost,
                    Discount = src.Discount,
                    TaxRate = src.TaxRate,
                    RefPurchaseLineId = src.Id
                };

                result.Add(line);
            }

            if (result.Count == 0)
                throw new InvalidOperationException("Return must contain at least one non-zero line.");

            return result;
        }

        private async Task EnforceOnHandCapsAsync(
            PosClientDbContext db,
            Purchase header,
            IReadOnlyCollection<PurchaseLine> lines,
            CancellationToken ct)
        {
            var (locType, locId) = ResolveSource(header);

            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var onHand = await _invRead.GetOnHandBulkAsync(itemIds, locType, locId, DateTime.UtcNow, ct);

            foreach (var l in lines)
            {
                var reqAbs = Math.Abs(l.Qty);
                var avail = onHand.TryGetValue(l.ItemId, out var v) ? v : 0m;
                if (reqAbs > avail)
                    throw new InvalidOperationException(
                        $"Return qty for item {l.ItemId} exceeds available stock at source. Requested={reqAbs}, OnHand={avail}.");
            }
        }

        private async Task EnforceOnHandCapsForAmendAsync(
            PosClientDbContext db,
            Purchase header,
            IReadOnlyCollection<PurchaseLine> oldLines,
            IReadOnlyCollection<PurchaseLine> newLines,
            CancellationToken ct)
        {
            var (locType, locId) = ResolveSource(header);

            var itemIds = oldLines
                .Concat(newLines)
                .Select(l => l.ItemId)
                .Distinct()
                .ToList();

            var onHand = await _invRead.GetOnHandBulkAsync(itemIds, locType, locId, DateTime.UtcNow, ct);

            var oldByItem = oldLines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var newByItem = newLines
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            foreach (var itemId in itemIds)
            {
                oldByItem.TryGetValue(itemId, out var oldQty);
                newByItem.TryGetValue(itemId, out var newQty);

                var delta = newQty - oldQty; // negative = more stock out
                if (delta >= 0) continue;    // making it less negative or same -> safe

                var extraOut = Math.Abs(delta);
                var avail = onHand.TryGetValue(itemId, out var v) ? v : 0m;

                if (extraOut > avail)
                    throw new InvalidOperationException(
                        $"Amendment would make stock negative for item {itemId}. ExtraOut={extraOut}, OnHand={avail}.");
            }
        }

        private static (InventoryLocationType locType, int locId) ResolveSource(Purchase p)
        {
            if (p.LocationType == InventoryLocationType.Outlet)
            {
                if (p.OutletId is null or 0)
                    throw new InvalidOperationException("OutletId is required when LocationType=Outlet.");
                return (InventoryLocationType.Outlet, p.OutletId.Value);
            }

            if (p.LocationType == InventoryLocationType.Warehouse)
            {
                if (p.WarehouseId is null or 0)
                    throw new InvalidOperationException("WarehouseId is required when LocationType=Warehouse.");
                return (InventoryLocationType.Warehouse, p.WarehouseId.Value);
            }

            throw new InvalidOperationException("Unsupported LocationType for Purchase Return.");
        }

        private static void NormalizeAndComputeReturn(
            Purchase header,
            IReadOnlyList<PurchaseLine> lines,
            IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds)
        {
            var subtotal = lines.Sum(l => l.UnitCost * Math.Abs(l.Qty)); // your rule; keep your existing math if different
            var tax = 0m; // as per your model
            var discount = 0m;

            header.Subtotal = subtotal;
            header.Tax = tax;
            header.Discount = discount;
            header.GrandTotal = subtotal + tax - discount + header.OtherCharges;

            var refundsTotal = refunds?.Where(r => r.amount > 0m).Sum(r => r.amount) ?? 0m;

            header.CashPaid = Math.Min(refundsTotal, header.GrandTotal);  // paid is exactly the sum of refunds
            header.CreditDue = Math.Max(0m, header.GrandTotal - header.CashPaid);
        }

        private static void ApplyRefundsToPayments(
    Purchase p,
    IEnumerable<(TenderMethod method, decimal amount, string? note)> refunds,
    string user,
    DateTime nowUtc)
        {
            p.Payments ??= new List<PurchasePayment>();
            p.Payments.Clear();

            foreach (var (method, amount, note) in refunds.Where(r => r.amount > 0m))
            {
                p.Payments.Add(new PurchasePayment
                {
                    Purchase = p,
                    SupplierId = p.PartyId,
                    OutletId = p.OutletId,
                    WarehouseId = p.WarehouseId,
                    Kind = PurchasePaymentKind.OnReceive,
                    Method = method,
                    Amount = amount,
                    Note = note,
                    IsEffective = true,
                    CreatedAtUtc = nowUtc,
                    CreatedBy = user,
                    UpdatedAtUtc = nowUtc,
                    UpdatedBy = user
                });
            }
        }

        private static void GuardBankRefundPresence(Purchase p)
        {
            foreach (var pay in p.Payments ?? Enumerable.Empty<PurchasePayment>())
            {
                if (pay.Method == TenderMethod.Bank && (!pay.BankAccountId.HasValue || pay.BankAccountId.Value <= 0))
                    throw new InvalidOperationException("Bank refund selected but no bank account was chosen.");
            }
        }

        private static async Task ValidateBankRefundAccountsAsync(PosClientDbContext db, Purchase p, CancellationToken ct)
        {
            foreach (var pay in p.Payments ?? Enumerable.Empty<PurchasePayment>())
            {
                if (pay.Method != TenderMethod.Bank || !pay.BankAccountId.HasValue) continue;

                var bId = pay.BankAccountId.Value;
                var meta = await db.Accounts.AsNoTracking()
                    .Where(a => a.Id == bId)
                    .Select(a => new { a.Id, a.AllowPosting, a.IsHeader, a.Type })
                    .FirstOrDefaultAsync(ct);

                if (meta is null)
                    throw new InvalidOperationException($"Selected bank account (Id={bId}) not found.");

                if (!meta.AllowPosting || meta.IsHeader)
                    throw new InvalidOperationException($"Selected bank account (Id={bId}) is not a leaf/postable account.");

                if (meta.Type != AccountType.Asset)
                    throw new InvalidOperationException($"Selected bank account (Id={bId}) must be an Asset type.");
            }
        }



        private static void UpsertReturnHeaderAndLines(
    PosClientDbContext db,
    Purchase header,
    List<PurchaseLine> lines,
    DateTime nowUtc,
    string user)
        {
            bool isNew = header.Id == 0;

            // --- header normalization ---
            header.IsReturn = true;
            header.Status = PurchaseStatus.Final;
            header.PurchaseDate = header.PurchaseDate == default ? nowUtc : header.PurchaseDate;
            header.ReceivedAtUtc ??= nowUtc;

            header.UpdatedAtUtc = nowUtc;
            header.UpdatedBy = user;

            if (isNew)
            {
                header.CreatedAtUtc = nowUtc;
                header.CreatedBy = user;

                // Ensure DocNo exists for returns so the document appears in the center
                if (string.IsNullOrWhiteSpace(header.DocNo))
                {
                    string NextReturnNo()
                    {
                        var year = nowUtc.Year;
                        var outletId = header.OutletId ?? 0;

                        // Count only finalized returns for this outlet & year
                        var count = db.Purchases.AsNoTracking().Count(p =>
                            p.IsReturn == true &&
                            p.Status != PurchaseStatus.Draft &&
                            p.OutletId == outletId &&
                            p.CreatedAtUtc.Year == year);

                        return $"PR-{year:0000}-{count + 1:00000}";
                    }

                    header.DocNo = NextReturnNo();
                }

                db.Purchases.Add(header);
            }
            else
            {
                db.Purchases.Update(header);

                // remove persisted lines and clear in-memory collection (avoid modifying during enumeration)
                var existing = db.PurchaseLines.Where(l => l.PurchaseId == header.Id).ToList();
                if (existing.Count != 0)
                    db.PurchaseLines.RemoveRange(existing);

                header.Lines.Clear();
            }

            // --- attach incoming lines via the collection so EF sets the REQUIRED FK (PurchaseId) ---
            foreach (var l in lines)
            {
                l.Id = 0;
                l.CreatedAtUtc = l.CreatedAtUtc == default ? nowUtc : l.CreatedAtUtc;
                l.UpdatedAtUtc = null;
                l.CreatedBy = l.CreatedBy ?? user;
                l.UpdatedBy = null;

                // IMPORTANT: add through the navigation collection (no explicit PurchaseId set)
                header.Lines.Add(l);
            }
        }


        private static void DumpTrackedFkValuesSafe(PosClientDbContext db, string trace)
        {
            // Snapshot first so we don't enumerate a live collection that will change underneath.
            var entries = db.ChangeTracker.Entries().ToList();

            foreach (var entry in entries)
            {
                var et = entry.Metadata;
                foreach (var fk in et.GetForeignKeys())
                {
                    var depVals = fk.Properties
                                    .Select(p => $"{p.Name}={(entry.Property(p.Name).CurrentValue ?? "<null>")}")
                                    .ToArray();

                    // IMPORTANT: do NOT call Find/Any/Load here — those change tracking state.
                    Debug.WriteLine($"{trace} TRK {et.ClrType.Name} state={entry.State} FK[{string.Join(", ", depVals)}] -> {fk.PrincipalEntityType.ClrType.Name}");
                }
            }
        }


        //FKs for key entities (once per run is enough)
        private static void DumpModelFks(PosClientDbContext db, string trace)
    {
        void DumpFor(string name, IEntityType et)
        {
            Debug.WriteLine($"{trace} MODEL: {name} -> table '{et.GetTableName()}'");
            foreach (var fk in et.GetForeignKeys())
            {
                var depProps = string.Join(",", fk.Properties.Select(p => p.Name));
                var principal = fk.PrincipalEntityType.ClrType.Name;
                var pk = string.Join(",", fk.PrincipalKey.Properties.Select(p => p.Name));
                Debug.WriteLine($"{trace}   FK dep=[{depProps}] -> {principal}([{pk}]) " +
                    $"req={fk.IsRequired} uniq={fk.IsUnique} onDel={fk.DeleteBehavior}");
            }
        }

        var model = db.Model;
        DumpFor("PurchaseLine", model.FindEntityType(typeof(PurchaseLine))!);
        DumpFor("PurchasePayment", model.FindEntityType(typeof(PurchasePayment))!);
        DumpFor("Purchase", model.FindEntityType(typeof(Purchase))!);
    }

    // For each tracked entry, print every FK’s dependent values and whether the principal exists
    //private static async Task DumpTrackedFkValuesAsync(PosClientDbContext db, string trace, CancellationToken ct)
    //{
    //    foreach (var entry in db.ChangeTracker.Entries())
    //    {
    //        var et = entry.Metadata;
    //        foreach (var fk in et.GetForeignKeys())
    //        {
    //            // dependent values
    //            var depVals = fk.Properties
    //                            .Select(p => $"{p.Name}={(entry.Property(p.Name).CurrentValue ?? "<null>")}")
    //                            .ToArray();
    //            // build lookup for principal (if all dep props have values)
    //            bool principalExists = false;
    //            try
    //            {
    //                var depHasAll = fk.Properties.All(p => entry.Property(p.Name).CurrentValue != null);
    //                if (depHasAll)
    //                {
    //                    // make anonymous key object for FindAsync
    //                    var keyVals = fk.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();
    //                    var set = db.GetType()
    //                                .GetMethod("Set", Type.EmptyTypes)!
    //                                .MakeGenericMethod(fk.PrincipalEntityType.ClrType)
    //                                .Invoke(db, null);
    //                    var findAsync = set!.GetType().GetMethod("FindAsync", new[] { typeof(object[]) });
    //                    var task = (dynamic)findAsync!.Invoke(set, new object[] { keyVals })!;
    //                    var result = await task;
    //                    principalExists = (result != null);
    //                }
    //            }
    //            catch { /* best-effort */ }

    //            Debug.WriteLine($"{trace} TRK {et.ClrType.Name} state={entry.State} FK[{string.Join(",", depVals)}] -> {fk.PrincipalEntityType.ClrType.Name} exists={principalExists}");
    //        }
    //    }
    //}

    // ChangeTracker dump already present, keep using yours:
    private void DumpChangeTracker(PosClientDbContext db, string tag)
    {
        try
        {
            foreach (var e in db.ChangeTracker.Entries())
            {
                Debug.WriteLine(
                    $"[{tag}] ET={e.Entity.GetType().Name} State={e.State} Values={string.Join(", ", e.CurrentValues.Properties.Select(p => p.Name + "=" + (e.CurrentValues[p]?.ToString() ?? "<null>")))}");
            }
        }
        catch { }
    }

    // Optional: show which exact property set has a default/zero that will violate an FK
    private static void DumpZeroOrNullFks(PosClientDbContext db, string trace)
    {
        foreach (var entry in db.ChangeTracker.Entries())
        {
            var et = entry.Metadata;
            foreach (var fk in et.GetForeignKeys())
            {
                foreach (var p in fk.Properties)
                {
                    var v = entry.Property(p.Name).CurrentValue;
                    var isBad = v == null || (v is int i && i == 0) || (v is long l && l == 0);
                    if (isBad)
                    {
                        Debug.WriteLine($"{trace} BAD-FK {et.ClrType.Name}.{p.Name} = {(v ?? "<null>")} (FK to {fk.PrincipalEntityType.ClrType.Name})");
                    }
                }
            }
        }
    }

    private static void DebugWriteSaveFailure(DbUpdateException ex, string trace)
    {
        Debug.WriteLine($"{trace} SAVE FAILED: {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            Debug.WriteLine($"{trace} INNER: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    }

        // Pos.Persistence/Services/PurchaseReturnsService.cs
        public async Task<bool> HasActiveReturnsAsync(int purchaseId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Purchases
                .AsNoTracking()
                .AnyAsync(x => x.IsReturn
                            && x.RefPurchaseId == purchaseId
                            && x.Status != PurchaseStatus.Voided, ct);
        }


    }
}
