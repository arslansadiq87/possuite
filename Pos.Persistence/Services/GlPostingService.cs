// Pos.Persistence/Services/GlPostingService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using Microsoft.VisualBasic;
using Pos.Domain;
using Pos.Domain.Abstractions;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Hr;
using Pos.Domain.Services;
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class GlPostingService : IGlPostingService, IGlPostingServiceDb
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ICoaService _coa;

        public GlPostingService(
            IDbContextFactory<PosClientDbContext> dbf,
            ICoaService coa,
            IInvoiceSettingsService _ignore,
            IOutboxWriter _ignore2)
        {
            _dbf = dbf;
            _coa = coa;
        }

        // -------------------- helpers --------------------
        private static GlEntry Row(
            DateTime tsUtc,
            DateTime effectiveDate,
            int? outletId,
            int accountId,
            decimal debit,
            decimal credit,
            GlDocType docType,
            GlDocSubType subType,
            int docId,
            string? docNo,
            Guid chainId,
            int? partyId,
            string? memo) => new()
            {
                TsUtc = tsUtc,
                EffectiveDate = effectiveDate,
                OutletId = outletId,
                AccountId = accountId,
                Debit = Math.Round(debit, 2),
                Credit = Math.Round(credit, 2),
                DocType = docType,
                DocSubType = subType,
                DocId = docId,
                DocNo = docNo,
                ChainId = chainId,
                IsEffective = true,
                PartyId = partyId,
                Memo = memo
            };


        // GlPostingService.cs
        private GlEntry Row(
            DateTime tsUtc,
            DateTime effectiveDate,
            int? outletId,
            int accountId,
            decimal debit,
            decimal credit,
            GlDocType docType,
            GlDocSubType subType,
            Sale s,
            int? partyId = null,
            string? memo = null)
        {
            return new GlEntry
            {
                TsUtc = tsUtc,
                OutletId = outletId,
                AccountId = accountId,
                Debit = debit,
                Credit = credit,
                DocType = docType,
                DocSubType = subType,

                // link to Sale doc
                DocId = s.Id,
                DocNo = s.InvoiceNumber.ToString(), // or s.DocNo if you keep both
                PublicId = s.PublicId,
                ChainId = s.PublicId,

                PartyId = partyId ?? s.CustomerId,

                EffectiveDate = effectiveDate,
                IsEffective = true,

                CreatedAtUtc = tsUtc,
                CreatedBy = s.UpdatedBy ?? s.CreatedBy,
                UpdatedAtUtc = tsUtc,
                UpdatedBy = s.UpdatedBy ?? s.CreatedBy,

                Memo = memo
            };
        }

        private GlEntry Row(
            int accountId,
            decimal debit,
            decimal credit,
            GlDocType docType,
            GlDocSubType subType,
            Sale s,
            DateTime tsUtc,
            DateTime effectiveDate,
            int? outletId,
            int? partyId = null,
            string? memo = null)
            => Row(tsUtc, effectiveDate, outletId, accountId, debit, credit, docType, subType, s, partyId, memo);

             

        // Add near top (helpers region)
        private static IEnumerable<GlEntry> PaymentRows(
            DateTime tsUtc,
            DateTime effDate,
            int outletId,
            int apAccId,
            int? cashAccId,
            int? bankAccId,
            int? otherAccId,
            Purchase p,
            PurchasePayment pay,
            int sign // +1 normal, -1 reversal
        )
        {
            if (!pay.IsEffective || pay.Amount <= 0m)
                yield break;

            var amt = Math.Round(pay.Amount, 2) * sign;

            // 1) AP DR (reduce payable)
            {
                var e1 = Row(
                    tsUtc, effDate, outletId, apAccId,
                    debit: amt > 0 ? amt : 0m,
                    credit: amt < 0 ? -amt : 0m,
                    GlDocType.Purchase, GlDocSubType.Purchase_Payment,
                    p.Id, p.PublicId.ToString(), p.PublicId, p.PartyId,
                    $"Supplier payment [{pay.Method}] · PayId={pay.Id}"
                );
                e1.LinkedPaymentId = pay.Id; // <-- set explicitly
                yield return e1;
            }

            // 2) Counter-account (Cash/Bank/Other) CR
            int? acct = pay.Method switch
            {
                TenderMethod.Cash => cashAccId,
                TenderMethod.Bank or TenderMethod.Card => bankAccId,
                _ => otherAccId
            };
            if (!acct.HasValue)
                throw new InvalidOperationException("Missing Cash/Bank/Other ledger account.");

            {
                var e2 = Row(
                    tsUtc, effDate, outletId, acct.Value,
                    debit: amt < 0 ? -amt : 0m,
                    credit: amt > 0 ? amt : 0m,
                    GlDocType.Purchase, GlDocSubType.Purchase_Payment,
                    p.Id, p.PublicId.ToString(), p.PublicId, p.PartyId,
                    $"Payment out [{pay.Method}] · PayId={pay.Id}"
                );
                e2.LinkedPaymentId = pay.Id; // <-- set explicitly
                yield return e2;
            }
        }

        private static int RequireBankAccountId(PurchasePayment pay)
        {
            if (pay.BankAccountId.HasValue && pay.BankAccountId.Value > 0)
                return pay.BankAccountId.Value;

            throw new InvalidOperationException("Bank payment/refund requires a specific BankAccountId (no fallback to header 113).");
        }

        private async Task<int> ResolveOutletCashInHandAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            return await _coa.EnsureOutletCashAccountAsync(outletId, ct);
        }




        private async Task<int> ResolveTillAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            // Try outlet-specific till via CoA service first
            var tillId = await _coa.GetTillAccountIdAsync(outletId, ct); // returns int

            if (tillId != 0)
                return tillId;

            // Fallbacks: try standard cash/till control codes (adjust if your CoA uses different codes)
            // 112: Cash in Till  | 111: Cash in Hand
            var candidates = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "112" || a.Code == "111")
                .Select(a => new { a.Code, a.Id })
                .ToListAsync(ct);

            var id = candidates.FirstOrDefault(x => x.Code == "112")?.Id
                  ?? candidates.FirstOrDefault(x => x.Code == "111")?.Id
                  ?? 0;

            if (id == 0)
                throw new InvalidOperationException("Till/Cash account missing (expected code 112 or 111). Configure outlet till in CoA.");

            return id;
        }


        private async Task<int> ResolveCashAsync(PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            // Prefer your CoA service mapping (e.g., 111-<OutletCode>)
            var cashId = await _coa.GetCashAccountIdAsync(outletId, ct); // returns int
            if (cashId != 0)
                return cashId;

            // Fallback: any postable leaf under 111
            var fallbackId = await db.Accounts.AsNoTracking()
                .Where(a => !a.IsHeader && a.AllowPosting && (a.Code == "111" || a.Code.StartsWith("111-")))
                .OrderBy(a => a.Code)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (fallbackId == 0)
                throw new InvalidOperationException("Cash-in-Hand account (code 111 or 111-*) not found or not postable. Configure CoA or preferences.");

            return fallbackId;
        }




        private static async Task InvalidateChainAsync(PosClientDbContext db, Guid chainId, CancellationToken ct)
        {
            await db.GlEntries
                .Where(g => g.ChainId == chainId && g.IsEffective)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);
        }

        private static (decimal cash, decimal bank, decimal other) SumPayments(IEnumerable<PurchasePayment>? pays)
        {
            decimal cash = 0, bank = 0, other = 0;
            if (pays == null) return (0, 0, 0);
            foreach (var p in pays)
            {
                if (p.Amount <= 0) continue;
                switch (p.Method)
                {
                    case TenderMethod.Cash: cash += p.Amount; break;
                    case TenderMethod.Bank:
                    case TenderMethod.Card: bank += p.Amount; break;
                    default: other += p.Amount; break;
                }
            }
            return (cash, bank, other);
        }

        private static DateTime TsUtc(BaseEntity e) => e.UpdatedAtUtc ?? e.CreatedAtUtc;
        private static DateTime EffDate(BaseEntity e) => (e.UpdatedAtUtc ?? e.CreatedAtUtc).Date;

        private static async Task<int> RequireAccountByCodeAsync(PosClientDbContext db, string code, CancellationToken ct)
        {
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (id == 0) throw new InvalidOperationException($"Chart of Accounts is missing required account code '{code}'.");
            return id;
        }

     


        private static async Task<int> ResolveApAsync(PosClientDbContext db, Party party, CancellationToken ct)
        {
            if (party.AccountId is int pid && pid != 0) return pid;
            return await RequireAccountByCodeAsync(db, "6100", ct); // AP root
        }

        public async Task PostOpeningStockAsync(StockDoc doc, IEnumerable<StockEntry> openingEntries, int offsetAccountId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostOpeningStockAsync(db, doc, openingEntries, offsetAccountId, ct);

        }

        public async Task UnlockOpeningStockAsync(StockDoc doc, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await UnlockOpeningStockAsync(db, doc, ct);
        }
        
        public async Task PostPurchaseReturnAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchaseReturnAsync(db, p, ct);
        }

        //public async Task PostPurchaseVoidAsync(Purchase p, CancellationToken ct = default)
        //{
        //    await using var db = await _dbf.CreateDbContextAsync(ct);
        //    await PostPurchaseVoidAsync(db, p, ct);
        //}

        public async Task PostPurchaseReturnVoidAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchaseReturnVoidAsync(db, p, ct);
        }

        public async Task PostPurchasePaymentAddedAsync(Purchase p, PurchasePayment pay, CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchasePaymentAddedAsync(p, pay, ct);
        }

        public async Task PostPurchasePaymentReversalAsync(Purchase p, PurchasePayment oldPay, CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchasePaymentReversalAsync(db, p, oldPay, ct);
        }

        private static async Task<Party> RequirePartyAsync(PosClientDbContext db, int partyId, CancellationToken ct)
        {
            var party = await db.Parties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partyId, ct);
            if (party is null) throw new InvalidOperationException($"Supplier not found (PartyId={partyId}).");
            return party;
        }

        private static async Task<(InventoryLocationType LType, int LocationId)> RequirePurchaseLocationAsync(
            PosClientDbContext db, Purchase p, CancellationToken ct)
        {
            if (p.LocationType == InventoryLocationType.Outlet)
            {
                if (p.OutletId is null or 0) throw new InvalidOperationException("Outlet is required.");
                var ok = await db.Outlets.AsNoTracking().AnyAsync(o => o.Id == p.OutletId, ct);
                if (!ok) throw new InvalidOperationException($"Outlet not found (Id={p.OutletId}).");
                return (InventoryLocationType.Outlet, p.OutletId.Value);
            }
            if (p.LocationType == InventoryLocationType.Warehouse)
            {
                if (p.WarehouseId is null or 0) throw new InvalidOperationException("Warehouse is required.");
                var ok = await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == p.WarehouseId, ct);
                if (!ok) throw new InvalidOperationException($"Warehouse not found (Id={p.WarehouseId}).");
                return (InventoryLocationType.Warehouse, p.WarehouseId.Value);
            }
            throw new InvalidOperationException("Invalid purchase location type.");
        }


        private static async Task<int> RequireOutletIdAsync(PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            if (outletId is null || outletId.Value == 0)
                throw new InvalidOperationException("Outlet is required on Purchase.");
            var exists = await db.Outlets.AsNoTracking()
                                         .AnyAsync(o => o.Id == outletId.Value, ct);
            if (!exists)
                throw new InvalidOperationException($"Outlet not found (OutletId={outletId}).");
            return outletId.Value;
        }

        // Gross: Inventory DR, Supplier CR (2 rows)
        public async Task PostPurchaseAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchaseAsync(db, p, ct);
        }


        // -------------------- Purchases (DB overloads) --------------------
        // Gross posting (Inventory DR, Supplier CR). No payment rows here.
        // ======= REPLACE THIS METHOD =======
        public async Task PostPurchaseAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            var tsUtc = DateTime.UtcNow;
            var eff = p.ReceivedAtUtc ?? tsUtc;
            var outletId = p.OutletId;

            // Resolve Inventory account with THIS db (no CoA calls that open new contexts)
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.OutletId == null && !a.IsHeader && a.AllowPosting
                            && a.Type == AccountType.Asset && a.Code == "1140")
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (inventoryAccId == 0)
                throw new InvalidOperationException("Inventory account not found. Create a company-level posting account named 'Inventory'.");

            // Resolve Supplier account from Party.AccountId with THIS db
            var supplierAccId = await db.Parties.AsNoTracking()
                .Where(x => x.Id == p.PartyId)
                .Select(x => x.AccountId)
                .FirstOrDefaultAsync(ct);
            if (!supplierAccId.HasValue)
                throw new InvalidOperationException("Supplier Party.AccountId is missing. Link supplier to an account before posting.");

            // Re-write gross snapshot: inactivate previous gross for this chain, then write current 2 rows
            await InactivateEffectiveAsync(db, p.PublicId, GlDocType.Purchase, GlDocSubType.Purchase_Gross, ct);

            // Inactivate previous GROSS snapshot by ChainId OR DocId (belt & suspenders)
            await db.GlEntries
                .Where(e =>
                    e.IsEffective &&
                    e.DocType == GlDocType.Purchase &&
                    e.DocSubType == GlDocSubType.Purchase_Gross &&
                    (
                        e.ChainId == p.PublicId ||            // preferred path (stable chain)
                        e.DocId == p.Id                     // fallback if chain ever drifted
                    ))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsEffective, false), ct);


            await db.GlEntries.AddRangeAsync(new[]
            {
                Row(tsUtc, eff, outletId, inventoryAccId,
                    debit: p.GrandTotal, credit: 0m, GlDocType.Purchase, GlDocSubType.Purchase_Gross, p),

                Row(tsUtc, eff, outletId, supplierAccId.Value,
                    debit: 0m, credit: p.GrandTotal, GlDocType.Purchase, GlDocSubType.Purchase_Gross, p, partyId: p.PartyId),
            }, ct);

            // IMPORTANT: do NOT SaveChanges here; caller controls transaction/UoW
        }

        // ======= REPLACE THIS METHOD =======
        public async Task PostPurchaseRevisionAsync(PosClientDbContext db, Purchase amended, decimal deltaGrand, CancellationToken ct = default)
        {
            if (deltaGrand == 0m) return;

            var tsUtc = DateTime.UtcNow;
            var eff = amended.UpdatedAtUtc ?? tsUtc;
            var outletId = amended.OutletId;

            // Resolve Inventory account with THIS db
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.OutletId == null && !a.IsHeader && a.AllowPosting
                            && a.Type == AccountType.Asset && a.Name == "Inventory")
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (inventoryAccId == 0)
                throw new InvalidOperationException("Inventory account not found. Create 'Inventory' account before posting.");

            // Resolve Supplier account with THIS db
            var supplierAccId = await db.Parties.AsNoTracking()
                .Where(x => x.Id == amended.PartyId)
                .Select(x => x.AccountId)
                .FirstOrDefaultAsync(ct);
            if (!supplierAccId.HasValue)
                throw new InvalidOperationException("Supplier Party.AccountId is missing. Link supplier to an account before posting.");

            // Delta-only rows — DO NOT inactivate gross here
            if (deltaGrand > 0m)
            {
                await db.GlEntries.AddRangeAsync(new[]
                {
            Row(tsUtc, eff, outletId, inventoryAccId, debit: deltaGrand, credit: 0m,
                GlDocType.PurchaseRevision, GlDocSubType.Purchase_AmendDelta, amended),
            Row(tsUtc, eff, outletId, supplierAccId.Value, debit: 0m, credit: deltaGrand,
                GlDocType.PurchaseRevision, GlDocSubType.Purchase_AmendDelta, amended, partyId: amended.PartyId),
        }, ct);
            }
            else
            {
                var v = Math.Abs(deltaGrand);
                await db.GlEntries.AddRangeAsync(new[]
                {
            Row(tsUtc, eff, outletId, inventoryAccId, debit: 0m, credit: v,
                GlDocType.PurchaseRevision, GlDocSubType.Purchase_AmendDelta, amended),
            Row(tsUtc, eff, outletId, supplierAccId.Value, debit: v, credit: 0m,
                GlDocType.PurchaseRevision, GlDocSubType.Purchase_AmendDelta, amended, partyId: amended.PartyId),
        }, ct);
            }
        }


        // Payment delta: Supplier DR vs counter (CR). Counter is cash-in-hand OR bank.
        public async Task PostPurchasePaymentAsync(Purchase p, int partyId, int counterAccountId, decimal amount, TenderMethod method, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var tsUtc = DateTime.UtcNow;
            var eff = tsUtc;
            var outletId = p.OutletId;

            // Inactivate previous effective "payment" rows (we re-write the total as of now)
            await InactivateEffectiveAsync(db, p.PublicId, GlDocType.Purchase, GlDocSubType.Purchase_Payment, ct);

            // 2 rows: Supplier DR, Counter CR
            await db.GlEntries.AddRangeAsync(new[]
            {
        Row(tsUtc, eff, outletId, await _coa.EnsureSupplierAccountIdAsync(partyId, ct),
            debit: amount, credit: 0m, GlDocType.Purchase, GlDocSubType.Purchase_Payment, p, partyId: partyId),
        Row(tsUtc, eff, outletId, counterAccountId,
            debit: 0m, credit: amount, GlDocType.Purchase, GlDocSubType.Purchase_Payment, p, partyId: partyId),
    }, ct);

            await db.SaveChangesAsync(ct);
        }



        // Revision delta: +/- Inventory vs Supplier (2 rows, delta only)
        public async Task PostPurchaseRevisionAsync(Purchase amended, decimal deltaGrand, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchaseRevisionAsync(db, amended, deltaGrand, ct);
        }

        // Revision: only delta of grand changes (positive=extra DR Inv / CR Supplier; negative reverse)
        

        public async Task PostPurchaseVoidAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            // Inactivate ALL effective rows in the chain (gross + payments + deltas)
            await InactivateEffectiveAsync(db, p.PublicId, type: null, sub: null, ct);
        }


        // Void: mark all chain rows ineffective (gross + payments)
        public async Task PostPurchaseVoidAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await InactivateEffectiveAsync(db, p.PublicId, null, null, ct); // all subtypes for this chain
            await db.SaveChangesAsync(ct);
        }

        // --------------- helpers ---------------
        private async Task InactivateEffectiveAsync(PosClientDbContext db, Guid chainId, GlDocType? type, GlDocSubType? sub, CancellationToken ct)
        {
            var q = db.GlEntries.Where(x => x.ChainId == chainId && x.IsEffective);
            if (type.HasValue) q = q.Where(x => x.DocType == type);
            if (sub.HasValue) q = q.Where(x => x.DocSubType == sub.Value);
            await q.ExecuteUpdateAsync(setters => setters.SetProperty(e => e.IsEffective, false), ct);
        }

        private GlEntry Row(DateTime tsUtc, DateTime effectiveDate, int? outletId, int accountId,
            decimal debit, decimal credit, GlDocType docType, GlDocSubType subType, Purchase p, int? partyId = null)
        {
            return new GlEntry
            {
                TsUtc = tsUtc,
                EffectiveDate = effectiveDate,
                OutletId = outletId,
                AccountId = accountId,
                Debit = Math.Round(debit, 2),
                Credit = Math.Round(credit, 2),
                DocType = docType,
                DocSubType = subType,
                DocId = p.Id,
                DocNo = p.DocNo,
                ChainId = p.PublicId,
                IsEffective = true,
                PartyId = partyId,
                Memo = subType.ToString()
            };
        }

        public async Task PostPurchaseReturnAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            if (!p.IsReturn) throw new InvalidOperationException("PostPurchaseReturnAsync requires IsReturn=true.");
            if (p.PartyId == 0) throw new InvalidOperationException("Purchase.PartyId is required.");
            if (p.OutletId is null or 0) throw new InvalidOperationException("Purchase.OutletId is required.");

            var party = await db.Parties.AsNoTracking().FirstAsync(x => x.Id == p.PartyId, ct);

            var ts = TsUtc(p);
            var ed = EffDate(p);
            var outletId = p.OutletId!.Value;
            var chainId = p.PublicId;
            var docNo = p.DocNo; // keep doc no if assigned; fallback to PublicId below

            if (string.IsNullOrWhiteSpace(docNo))
                docNo = p.PublicId.ToString();

            var gross = Math.Round(p.GrandTotal, 2);
            var invAccId = await RequireAccountByCodeAsync(db, "1140", ct);              // Inventory
            var apAccId = await ResolveApAsync(db, party, ct);                           // Supplier(AP)

            // Invalidate any effective rows for this chain first (gross + prior payments)
            await InvalidateChainAsync(db, chainId, ct);

            var rows = new List<GlEntry>
    {
        // Inventory out (CR) on return
        Row(ts, ed, outletId, invAccId, 0m, gross,
            GlDocType.PurchaseReturn, GlDocSubType.Purchase_Return, p.Id, docNo, chainId, p.PartyId,
            "Purchase return · Inventory out (gross)"),

        // AP reduced (DR)
        Row(ts, ed, outletId, apAccId, gross, 0m,
            GlDocType.PurchaseReturn, GlDocSubType.Purchase_Return, p.Id, docNo, chainId, p.PartyId,
            "Purchase return · AP reduced")
    };

            // --- Refund split (Cash/Bank/Other) ---
            var (refCash, refBank, refOther) = SumPayments(p.Payments);
            var refund = refCash + refBank + refOther;

            if (refund > 0m)
            {
                // AP CR to offset refund
                rows.Add(Row(ts, ed, outletId, apAccId, 0m, refund,
                    GlDocType.PurchaseReturn, GlDocSubType.Purchase_Payment, p.Id, docNo, chainId, p.PartyId,
                    "Supplier refunded at return"));

                // CASH -> Outlet Cash-in-Hand
                if (refCash > 0m)
                {
                    var cashHandAccId = await ResolveOutletCashInHandAsync(db, outletId, ct);
                    rows.Add(Row(ts, ed, outletId, cashHandAccId, refCash, 0m,
                        GlDocType.PurchaseReturn, GlDocSubType.Purchase_Payment, p.Id, docNo, chainId, p.PartyId,
                        "Cash in hand (refund)"));
                }

                // BANK -> must use specific bank account; NO fallback to 113
                if (refBank > 0m)
                {
                    // pick the FIRST bank payment’s concrete BankAccountId
                    var bankPay = (p.Payments ?? Enumerable.Empty<PurchasePayment>())
                        .FirstOrDefault(x => x.Method == TenderMethod.Bank && x.IsEffective && x.Amount > 0m);

                    var bankAccId = bankPay != null
                        ? RequireBankAccountId(bankPay)
                        : throw new InvalidOperationException("Bank refund present but no concrete BankAccountId was provided.");

                    rows.Add(Row(ts, ed, outletId, bankAccId, refBank, 0m,
                        GlDocType.PurchaseReturn, GlDocSubType.Purchase_Payment, p.Id, docNo, chainId, p.PartyId,
                        "Bank in (refund)"));
                }

                // OTHER -> reject or map explicitly (here we reject to avoid accidental header use)
                if (refOther > 0m)
                    throw new InvalidOperationException("Refund method 'Other' requires an explicit posting account; header 113 cannot be used.");
            }


            await db.GlEntries.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);
        }



        public async Task PostPurchaseReturnVoidAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            await InvalidateChainAsync(db, p.PublicId, ct);
            await db.SaveChangesAsync(ct);
        }

        // -------------------- Generic void helpers --------------------
        public async Task VoidChainAsync(Guid chainId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await InvalidateChainAsync(db, chainId, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task VoidChainWithReversalsAsync(Guid chainId, DateTime tsUtc, bool invalidateOriginalsAfter = false, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var effective = await db.GlEntries.AsNoTracking()
                .Where(g => g.ChainId == chainId && g.IsEffective)
                .ToListAsync(ct);

            if (effective.Count == 0) return;

            var reversals = effective.Select(e => new GlEntry
            {
                TsUtc = tsUtc,
                EffectiveDate = tsUtc.Date,
                OutletId = e.OutletId,
                AccountId = e.AccountId,
                Debit = e.Credit,
                Credit = e.Debit,
                DocType = e.DocType,
                DocSubType = GlDocSubType.Other, // you can add a dedicated Void subtype if desired
                DocId = e.DocId,
                DocNo = e.DocNo,
                ChainId = e.ChainId,
                IsEffective = true,
                PartyId = e.PartyId,
                Memo = (e.Memo == null) ? "VOID" : $"VOID · {e.Memo}"
            }).ToList();

            await db.GlEntries.AddRangeAsync(reversals, ct);

            if (invalidateOriginalsAfter)
            {
                await db.GlEntries
                    .Where(g => g.ChainId == chainId && g.IsEffective)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);

                // re-enable reversals as effective
                var reversalIds = reversals.Select(r => r.Id).ToList(); // Ids assigned after SaveChanges; leave as simple flow:
            }

            await db.SaveChangesAsync(ct);
        }

        // -------------------- Sales/Vouchers/Payroll stubs to satisfy IGlPostingService --------------------
     
        public Task PostVoucherAsync(Voucher v, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostVoucherVoidAsync(Voucher voucherToVoid, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostVoucherRevisionAsync(Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default) => Task.CompletedTask;

        public Task PostPayrollAccrualAsync(PayrollRun run, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostPayrollPaymentAsync(PayrollRun run, CancellationToken ct = default) => Task.CompletedTask;

        private static async Task<int> ResolveCashInHandAsync(PosClientDbContext db, CancellationToken ct)
        {
            // Prefer standard "111" (Cash in Hand)
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "111")
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (id == 0) throw new InvalidOperationException("CoA missing Cash in Hand (code 111).");
            return id;
        }

        /// <summary>Pick a reasonable Over/Short account. Tries common codes, falls back to 1999.</summary>
        private static async Task<int> ResolveOverShortAsync(PosClientDbContext db, CancellationToken ct)
        {
            var codes = new[] { "5599", "5590", "7999", "1999" }; // adjust to your CoA
            var id = await db.Accounts.AsNoTracking()
                .Where(a => codes.Contains(a.Code))
                .OrderBy(a => Array.IndexOf(codes, a.Code))
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (id == 0) throw new InvalidOperationException("CoA missing Cash Over/Short (expected one of 5599/5590/7999/1999).");
            return id;
        }

        // --------- Till Close (APP overload) ---------
        public async Task PostTillCloseAsync(TillSession session, decimal declaredToMove, decimal systemCash, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostTillCloseAsync(db, session, declaredToMove, systemCash, ct);
        }

        // --------- Till Close (DB overload) ---------
        public async Task PostTillCloseAsync(PosClientDbContext db, TillSession session, decimal declaredToMove, decimal systemCash, CancellationToken ct = default)
        {
            if (session.Id == 0) throw new InvalidOperationException("TillSession must be saved before GL posting.");
            if (session.OutletId == 0) throw new InvalidOperationException("TillSession.OutletId is required.");

            var outletId = session.OutletId;
            var counterId = session.CounterId;
            var tsUtc = session.CloseTs ?? DateTime.UtcNow;
            var effDate = tsUtc.Date;
            var chainId = session.PublicId;        // TillSession inherits BaseEntity → has PublicId
            var docNo = $"TILL-{session.Id}";

            // Accounts
            var tillAccId = await ResolveTillAsync(db, outletId, ct);      // per-outlet (memo includes counter)
            var handAccId = await ResolveCashInHandAsync(db, ct);          // 111
            var ovrSrtAcc = await ResolveOverShortAsync(db, ct);           // 5599/…/1999

            // 1) Move declared cash from Till → Cash in Hand
            var rows = new List<GlEntry>
    {
        Row(tsUtc, effDate, outletId, handAccId, declaredToMove, 0m,
            GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
            $"Till close · Counter #{counterId} · Declared move to Cash-in-Hand"),

        Row(tsUtc, effDate, outletId, tillAccId, 0m, declaredToMove,
            GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
            $"Till close · Counter #{counterId} · Declared cash out of Till")
    };

            // 2) Cash Over/Short so Till ends at opening float
            // diff = moved - should-have-moved
            var diff = declaredToMove - systemCash;

            if (diff != 0m)
            {
                if (diff > 0m)
                {
                    // Over (moved too much): DR Till, CR Over/Short (gain)
                    rows.Add(Row(tsUtc, effDate, outletId, tillAccId, diff, 0m,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        $"Till close · Counter #{counterId} · Over (+{diff:0.##}) adj to keep float"));
                    rows.Add(Row(tsUtc, effDate, outletId, ovrSrtAcc, 0m, diff,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        "Cash Over/Short (over)"));
                }
                else
                {
                    var abs = Math.Abs(diff);
                    // Short (moved too little): DR Over/Short (expense), CR Till
                    rows.Add(Row(tsUtc, effDate, outletId, ovrSrtAcc, abs, 0m,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        "Cash Over/Short (short)"));
                    rows.Add(Row(tsUtc, effDate, outletId, tillAccId, 0m, abs,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        $"Till close · Counter #{counterId} · Short (−{abs:0.##}) adj to keep float"));
                }
            }

            // TillClose is an “image” post for this session → invalidate older rows for this session chain
            await db.GlEntries
                .Where(g => g.ChainId == chainId && g.IsEffective)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);

            await db.GlEntries.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);
        }


        public async Task PostPurchasePaymentAddedAsync(PosClientDbContext db, Purchase p, PurchasePayment pay, CancellationToken ct)
        {
            var party = await db.Parties.AsNoTracking().FirstAsync(x => x.Id == p.PartyId, ct);
            var ts = TsUtc(pay);
            var ed = ts.Date;
            var outletId = p.OutletId!.Value;

            var apAccId = await ResolveApAsync(db, party, ct);
            var cashAccId = await ResolveOutletCashInHandAsync(db, outletId, ct);
            var bankAccId = (pay.Method == TenderMethod.Bank) ? RequireBankAccountId(pay) : 0;

            // Build rows (no till, no 113)
            var rows = new List<GlEntry>();

            if (pay.Method == TenderMethod.Cash)
            {
                rows.AddRange(PaymentRows(ts, ed, outletId, apAccId, cashAccId, /*bank*/0, /*other*/0, p, pay, +1));
            }
            else if (pay.Method == TenderMethod.Bank)
            {
                rows.AddRange(PaymentRows(ts, ed, outletId, apAccId, /*cash*/0, bankAccId, /*other*/0, p, pay, +1));
            }
            else
            {
                throw new InvalidOperationException("Only Cash and Bank are supported for Purchase payments.");
            }

            await db.GlEntries.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);

        }


        public async Task PostPurchasePaymentReversalAsync(PosClientDbContext db, Purchase p, PurchasePayment oldPay, CancellationToken ct)
        {
            var party = await db.Parties.AsNoTracking().FirstAsync(x => x.Id == p.PartyId, ct);
            var ts = DateTime.UtcNow;
            var ed = ts.Date;
            var outletId = p.OutletId!.Value;

            var apAccId = await ResolveApAsync(db, party, ct);
            var cashAccId = await ResolveOutletCashInHandAsync(db, outletId, ct);      // << use Cash-in-Hand
            var bankAccId = oldPay.BankAccountId.HasValue && oldPay.BankAccountId.Value > 0
                            ? oldPay.BankAccountId.Value
                            : await RequireAccountByCodeAsync(db, "113", ct);
            var otherAccId = bankAccId;

            var rows = PaymentRows(ts, ed, outletId, apAccId, cashAccId, bankAccId, otherAccId, p, oldPay, -1).ToList();
            await db.GlEntries.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);
        }

        // -------------------- Opening Stock --------------------
        public async Task PostOpeningStockAsync(
            PosClientDbContext db,
            StockDoc doc,
            IEnumerable<StockEntry> openingEntries,
            int offsetAccountId,
            CancellationToken ct = default)
        {
            if (doc.DocType != StockDocType.Opening)
                throw new InvalidOperationException("PostOpeningStockAsync called with non-Opening stock document.");

            var tsUtc = DateTime.UtcNow;
            var eff = doc.EffectiveDateUtc;

            // For GL, OutletId column is meaningful only for outlet-based locations.
            int? outletId = doc.LocationType == InventoryLocationType.Outlet
                ? doc.LocationId
                : (int?)null;

            // Compute total opening value = Σ (net qty per item * last unit cost)
            var totalValue = openingEntries
                .GroupBy(e => e.ItemId)
                .Select(g =>
                {
                    var netQty = g.Sum(x => x.QtyChange);
                    if (netQty == 0m) return 0m;

                    // last snapshot cost for this item
                    var last = g
                        .OrderBy(x => x.Id)   // Id is monotonic
                        .Last();

                    return netQty * last.UnitCost;
                })
                .Sum();

            // Clear any previous GL snapshot for this Opening chain
            await InactivateEffectiveAsync(db, doc.PublicId, GlDocType.StockAdjust, GlDocSubType.Other, ct);

            if (totalValue == 0m)
            {
                // Nothing to post; GL is effectively removed for this doc.
                await db.SaveChangesAsync(ct);
                return;
            }

            // Inventory account (same 1140 used for purchases)
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "1140" && !a.IsHeader && a.AllowPosting)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);

            if (inventoryAccId == 0)
                throw new InvalidOperationException(
                    "Inventory account '1140' not found. Create it before posting Opening Stock.");

            var chainId = doc.PublicId;
            var docId = doc.Id;
            string? docNo = null; // Opening Stock currently has no human-readable number; you can wire one later.

            var rows = new[]
            {
                // DR Inventory (1140)
                Row(tsUtc, eff, outletId, inventoryAccId,
                    debit: totalValue,
                    credit: 0m,
                    GlDocType.StockAdjust,
                    GlDocSubType.Other,
                    docId,
                    docNo,
                    chainId,
                    partyId: null,
                    memo: "Opening stock value"),

                // CR Opening Stock (location-specific child under 11411 / 11412)
                Row(tsUtc, eff, outletId, offsetAccountId,
                    debit: 0m,
                    credit: totalValue,
                    GlDocType.StockAdjust,
                    GlDocSubType.Other,
                    docId,
                    docNo,
                    chainId,
                    partyId: null,
                    memo: "Opening stock offset")
            };

            await db.GlEntries.AddRangeAsync(rows, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task UnlockOpeningStockAsync(
            PosClientDbContext db,
            StockDoc doc,
            CancellationToken ct = default)
        {
            if (doc.DocType != StockDocType.Opening)
                throw new InvalidOperationException("UnlockOpeningStockAsync called with non-Opening stock document.");

            // Mark all Opening Stock GL rows for this chain as non-effective.
            await InactivateEffectiveAsync(db, doc.PublicId, GlDocType.StockAdjust, GlDocSubType.Other, ct);
            await db.SaveChangesAsync(ct);
        }

        // -------------------- Sales (per-document gross snapshot) --------------------
        public async Task PostSaleAsync(Sale s, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostSaleAsync(db, s, ct);
            await db.SaveChangesAsync(ct);   // <-- persist the rows we just added
        }

        public async Task PostSaleReturnAsync(Sale s, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostSaleReturnAsync(db, s, ct);
            await db.SaveChangesAsync(ct);
        }

        public Task PostSaleRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default)
            => PostSaleDeltaAsync(amended, deltaSub, deltaTax, isReturn: false, ct);

        public Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default)
            => PostSaleDeltaAsync(amended, deltaSub, deltaTax, isReturn: true, ct);

        // -------- DB overloads (participate in caller's transaction/UoW) --------
        private async Task PostSaleAsync(PosClientDbContext db, Sale s, CancellationToken ct)
        {
            // Guard
            if (s.IsReturn) { await PostSaleReturnAsync(db, s, ct); return; }

            var tsUtc = DateTime.UtcNow;
            var eff = s.Ts == default ? tsUtc.Date : s.Ts.Date;
            var outletId = s.OutletId;

            // Accounts (resolve by CoA codes seeded in your template)
            var revenueId = await ResolveAccountByCodeAsync(db, "411", ct);   // Gross sales value
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct); // Sales tax payable (output), optional
            var tillId = await ResolveTillAsync(db, outletId, ct);            // prefers outlet till (112 child), fallback cash(111)
            var cashId = await ResolveCashAsync(db, outletId, ct);            // outlet cash in hand (111 child)
            var cardClearingId = await ResolveSalesCardClearingForOutletAsync(db, outletId, ct); // from InvoiceSettings (per-outlet or global)

            // Optional AR (customer account) when not fully paid and non-walkin
            int? customerAccId = null;
            if (s.CustomerKind != CustomerKind.WalkIn && s.CustomerId.HasValue)
                customerAccId = await db.Parties.AsNoTracking()
                    .Where(p => p.Id == s.CustomerId.Value)
                    .Select(p => p.AccountId)
                    .FirstOrDefaultAsync(ct);

            // Inactivate previous “gross” for this chain and rewrite snapshot
            await InactivateEffectiveAsync(db, s.PublicId, GlDocType.Sale, GlDocSubType.Sale_Gross, ct);

            // Split total into pieces
            var total = s.Total;                 // > 0 for sale
            var tax = Math.Max(0m, s.TaxTotal);  // defensive
            var net = total - tax;

            var paidCash = Math.Max(0m, s.CashAmount);
            var paidCard = Math.Max(0m, s.CardAmount);
            var paidTotal = paidCash + paidCard;
            var arPortion = Math.Max(0m, total - paidTotal);

            // Debits: Cash/Till, CardClearing, AR (for non-walk-in balance)
            var debitRows = new List<GlEntry>();

            if (paidCash > 0m)
            {
                // If a till session is present → post to Till; else → Cash-in-Hand
                var cashAccount = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0) ? tillId : cashId;
                debitRows.Add(Row(tsUtc, eff, outletId, cashAccount, debit: paidCash, credit: 0m, GlDocType.Sale, GlDocSubType.Sale_Receipt, s));
            }

            if (paidCard > 0m)
            {
                if (cardClearingId == 0)
                    throw new InvalidOperationException("Sales card clearing account is not configured (Preferences → Invoice Settings).");
                debitRows.Add(Row(tsUtc, eff, outletId, cardClearingId, debit: paidCard, credit: 0m, GlDocType.Sale, GlDocSubType.Sale_Receipt, s));
            }

            if (arPortion > 0m && customerAccId.HasValue)
            {
                debitRows.Add(Row(tsUtc, eff, outletId, customerAccId.Value, debit: arPortion, credit: 0m,
                                  GlDocType.Sale, GlDocSubType.Sale_Receipt, s, partyId: s.CustomerId));
            }

            // Credits: Tax payable, Revenue(net)
            var creditRows = new List<GlEntry>();
            if (tax > 0m && taxPayableId != 0)
                creditRows.Add(Row(tsUtc, eff, outletId, taxPayableId, debit: 0m, credit: tax, GlDocType.Sale, GlDocSubType.Sale_Gross, s));

            creditRows.Add(Row(tsUtc, eff, outletId, revenueId, debit: 0m, credit: net, GlDocType.Sale, GlDocSubType.Sale_Gross, s));

            await db.GlEntries.AddRangeAsync(debitRows.Concat(creditRows), ct);
            // do not SaveChanges here (caller controls commit)
        }

        private async Task PostSaleReturnAsync(PosClientDbContext db, Sale s, CancellationToken ct)
        {
            // A return in your system has Total < 0 (you sum ABS in Till code). We’ll work in absolutes for clarity.
            var tsUtc = DateTime.UtcNow;
            var eff = s.Ts == default ? tsUtc.Date : s.Ts.Date;
            var outletId = s.OutletId;

            var salesReturnId = await ResolveAccountByCodeAsync(db, "412", ct); // Sales returns
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct); // Sales tax payable (output)
            var tillId = await ResolveTillAsync(db, outletId, ct);
            var cashId = await ResolveCashAsync(db, outletId, ct);
            var cardClearingId = await ResolveSalesCardClearingForOutletAsync(db, outletId, ct);

            int? customerAccId = null;
            if (s.CustomerKind != CustomerKind.WalkIn && s.CustomerId.HasValue)
                customerAccId = await db.Parties.AsNoTracking()
                    .Where(p => p.Id == s.CustomerId.Value)
                    .Select(p => p.AccountId)
                    .FirstOrDefaultAsync(ct);

            await InactivateEffectiveAsync(db, s.PublicId, GlDocType.SaleReturn, GlDocSubType.Sale_Return, ct);

            var totalAbs = Math.Abs(s.Total);
            var taxAbs = Math.Abs(s.TaxTotal);
            var netAbs = Math.Max(0m, totalAbs - taxAbs);

            var refundCashAbs = Math.Abs(Math.Min(0m, s.CashAmount)); // if you store negative on return; else use Math.Abs(s.CashAmount)
            if (refundCashAbs == 0m) refundCashAbs = Math.Abs(s.CashAmount);
            var refundCardAbs = Math.Abs(s.CardAmount);
            var refundTotalAbs = refundCashAbs + refundCardAbs;
            var arReduceAbs = Math.Max(0m, totalAbs - refundTotalAbs);

            // Debits (reduce liabilities/increase contra-income): SalesReturns (debit), TaxPayable (debit)
            var debits = new List<GlEntry>
    {
        Row(tsUtc, eff, outletId, salesReturnId, debit: netAbs, credit: 0m, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s)
    };
            if (taxAbs > 0m && taxPayableId != 0)
                debits.Add(Row(tsUtc, eff, outletId, taxPayableId, debit: taxAbs, credit: 0m, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s));

            // Credits (refund to cash/till, card clearing, or reduce AR)
            var credits = new List<GlEntry>();
            if (refundCashAbs > 0m)
            {
                var cashAccount = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0) ? tillId : cashId;
                credits.Add(Row(tsUtc, eff, outletId, cashAccount, debit: 0m, credit: refundCashAbs, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s));
            }
            if (refundCardAbs > 0m)
            {
                if (cardClearingId == 0)
                    throw new InvalidOperationException("Sales card clearing account is not configured (Preferences → Invoice Settings).");
                credits.Add(Row(tsUtc, eff, outletId, cardClearingId, debit: 0m, credit: refundCardAbs, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s));
            }
            if (arReduceAbs > 0m && customerAccId.HasValue)
            {
                credits.Add(Row(tsUtc, eff, outletId, customerAccId.Value, debit: 0m, credit: arReduceAbs,
                                GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, partyId: s.CustomerId));
            }

            await db.GlEntries.AddRangeAsync(debits.Concat(credits), ct);
            // no SaveChanges here
        }

        private async Task PostSaleDeltaAsync(Sale amended, decimal deltaSub, decimal deltaTax, bool isReturn, CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var tsUtc = DateTime.UtcNow;
            var eff = amended.Ts == default ? tsUtc.Date : amended.Ts.Date;
            var outletId = amended.OutletId;

            var subType = isReturn ? GlDocSubType.Sale_Return : GlDocSubType.Sale_AmendDelta;
            var docType = isReturn ? GlDocType.SaleReturn : GlDocType.SaleRevision;

            var revenueId = await ResolveAccountByCodeAsync(db, isReturn ? "412" : "411", ct);
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct);

            // When delta is positive in SALE context → increase credit revenue/tax.
            // In RETURN context you passed deltaSub/deltaTax with sign already (call appropriately).
            var lines = new List<GlEntry>();

            if (deltaTax != 0m && taxPayableId != 0)
            {
                lines.Add(Row(tsUtc, eff, outletId, taxPayableId,
                    debit: deltaTax < 0 ? Math.Abs(deltaTax) : 0m,
                    credit: deltaTax > 0 ? deltaTax : 0m, docType, subType, amended));
            }

            if (deltaSub != 0m)
            {
                // For returns we hit 412; for normal sale we hit 411
                var debit = 0m; var credit = 0m;
                if (isReturn)
                    debit = deltaSub > 0 ? deltaSub : 0m; // increase SalesReturns
                else
                    credit = deltaSub > 0 ? deltaSub : 0m; // increase Sales revenue

                if (deltaSub < 0 && !isReturn) debit = Math.Abs(deltaSub); // reduce revenue
                if (deltaSub < 0 && isReturn) credit = Math.Abs(deltaSub); // reduce SalesReturns

                lines.Add(Row(tsUtc, eff, outletId, revenueId, debit, credit, docType, subType, amended));
            }

            if (lines.Count > 0)
                {
                await db.GlEntries.AddRangeAsync(lines, ct);
                await db.SaveChangesAsync(ct);
                }
        }

        // -------- local resolvers (no new DbContext!) --------
        private static async Task<int> ResolveAccountByCodeAsync(PosClientDbContext db, string code, CancellationToken ct)
        {
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && !a.IsHeader && a.AllowPosting)
                .Select(a => a.Id).FirstOrDefaultAsync(ct);
            if (id == 0) throw new InvalidOperationException($"CoA account code {code} not found or not postable.");
            return id;
        }

        private static async Task<int> ResolveOptionalAccountByCodeAsync(PosClientDbContext db, string code, CancellationToken ct)
        {
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && !a.IsHeader && a.AllowPosting)
                .Select(a => a.Id).FirstOrDefaultAsync(ct);
            return id; // 0 when missing → treated as optional
        }

        // Pull SalesCardClearingAccountId from per-outlet InvoiceSettings (fallback to global row)
        private static async Task<int> ResolveSalesCardClearingForOutletAsync(PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            int? id = null;

            if (outletId.HasValue)
            {
                id = await db.InvoiceSettings.AsNoTracking()
                    .Where(x => x.OutletId == outletId.Value)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(x => x.SalesCardClearingAccountId)
                    .FirstOrDefaultAsync(ct);
            }

            if (!id.HasValue || id.Value == 0)
            {
                id = await db.InvoiceSettings.AsNoTracking()
                    .Where(x => x.OutletId == null)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(x => x.SalesCardClearingAccountId)
                    .FirstOrDefaultAsync(ct);
            }

            return id ?? 0;
        }

        // Inactivate all effective GL rows for a sale chain (gross + receipts + deltas)
        private static async Task InactivateAllForChainAsync(PosClientDbContext db, Guid chainId, CancellationToken ct)
        {
            var rows = await db.GlEntries
                .Where(e => e.ChainId == chainId && e.IsEffective)   // ChainId: Guid; IsEffective: bool
                .ToListAsync(ct);

            if (rows.Count == 0) return;

            foreach (var r in rows)
                r.IsEffective = false;
        }

        public async Task VoidSaleAsync(Sale s, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await InactivateAllForChainAsync(db, s.PublicId, ct);   // s.PublicId is Guid — correct
            await db.SaveChangesAsync(ct);
        }



    }
}
