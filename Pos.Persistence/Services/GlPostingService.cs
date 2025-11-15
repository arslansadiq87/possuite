// Pos.Persistence/Services/GlPostingService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        public Task PostSaleAsync(Sale sale, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostSaleRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostSaleReturnAsync(Sale sale, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default) => Task.CompletedTask;

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


    }
}
