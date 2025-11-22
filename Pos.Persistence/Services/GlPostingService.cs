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
using Pos.Domain.Utils;
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
            IInvoiceSettingsLocalService _ignore,
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


        // fo sales
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
                DocId = s.Id,
                DocNo = Pos.Domain.Formatting.DocNoComposer.FromSale(s),  //s.InvoiceNumber.ToString(), // or s.DocNo if you keep both
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

        //private GlEntry Row(
        //    int accountId,
        //    decimal debit,
        //    decimal credit,
        //    GlDocType docType,
        //    GlDocSubType subType,
        //    Sale s,
        //    DateTime tsUtc,
        //    DateTime effectiveDate,
        //    int? outletId,
        //    int? partyId = null,
        //    string? memo = null)
        //    => Row(tsUtc, effectiveDate, outletId, accountId, debit, credit, docType, subType, s, partyId, memo);
             

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

        private static GlDocType MapVoucherDocType(VoucherType t) => t switch
        {
            VoucherType.Payment => GlDocType.CashPayment,
            VoucherType.Receipt => GlDocType.CashReceipt,
            _ => GlDocType.JournalVoucher
        };

        private static GlDocSubType MapVoucherBaseSubType(VoucherType t) => t switch
        {
            VoucherType.Payment => GlDocSubType.Voucher_Payment,
            VoucherType.Receipt => GlDocSubType.Voucher_Receipt,
            _ => GlDocSubType.Voucher_Journal
        };

        private static string ComposeVoucherDocNo(Voucher v)
        {
            // Prefer RefNo if provided, else VCH-<Id>
            return string.IsNullOrWhiteSpace(v.RefNo) ? $"VCH-{v.Id}" : v.RefNo!;
        }

        private static DateTime VoucherEffectiveDate(Voucher v)
        {
            // Keep date chosen in editor; keep time-of-day neutralized to “now” if needed
            return (v.TsUtc == default ? DateTime.UtcNow : v.TsUtc).Date;
        }

        private async Task<int> ResolveOutletCashAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            // Use your per-outlet 111-<OutletCode> account
            return await ResolveCashInHandForOutletAsync(db, outletId, ct);
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

        private async Task<int> ResolveCashAsync(PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            var cashId = await _coa.GetCashAccountIdAsync(outletId, ct); // returns int
            if (cashId != 0)
                return cashId;
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

        // Replace existing method
        private async Task<int> ResolveApAsync(PosClientDbContext db, Party party, CancellationToken ct)
        {
            // Prefer the supplier’s own ledger account if already linked
            if (party.AccountId is int pid && pid != 0)
                return pid;

            // Create (or fetch) a supplier ledger under Parties and return it
            // This matches your seeding/CoaService approach and avoids using a non-existent "6100".
            var id = await _coa.EnsureSupplierAccountIdAsync(party.Id, ct);
            return id;
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
        public async Task PostPurchaseAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            var tsUtc = DateTime.UtcNow;
            var eff = p.ReceivedAtUtc ?? tsUtc;
            var outletId = p.OutletId;
            // Resolve Inventory account with THIS db (no CoA calls that open new contexts)
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.OutletId == null && !a.IsHeader
                            && a.Type == AccountType.Asset && a.Code == "11421")
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
        }

        // ======= REPLACE THIS METHOD =======
        public async Task PostPurchaseRevisionAsync(PosClientDbContext db, Purchase amended, decimal deltaGrand, CancellationToken ct = default)
        {
            if (deltaGrand == 0m) return;
            var tsUtc = DateTime.UtcNow;
            var eff = amended.UpdatedAtUtc ?? tsUtc;
            var outletId = amended.OutletId;
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.OutletId == null && !a.IsHeader 
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

        public async Task PostPurchasePaymentAsync(Purchase p, int partyId, int counterAccountId, decimal amount, TenderMethod method, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var tsUtc = DateTime.UtcNow;
            var eff = tsUtc;
            var outletId = p.OutletId;
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

        public async Task PostPurchaseRevisionAsync(Purchase amended, decimal deltaGrand, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostPurchaseRevisionAsync(db, amended, deltaGrand, ct);
        }

        public async Task PostPurchaseVoidAsync(PosClientDbContext db, Purchase p, CancellationToken ct = default)
        {
            await InactivateEffectiveAsync(db, p.PublicId, type: null, sub: null, ct);
        }

        public async Task PostPurchaseVoidAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await InactivateEffectiveAsync(db, p.PublicId, null, null, ct); // all subtypes for this chain
            await db.SaveChangesAsync(ct);
        }

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
                DocNo = Pos.Domain.Formatting.DocNoComposer.FromPurchase(p),
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
            var docNo = Pos.Domain.Formatting.DocNoComposer.FromPurchase(p);
            var gross = Math.Round(p.GrandTotal, 2);
            var invAccId = await RequireAccountByCodeAsync(db, "11422", ct);              // Inventory
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
        
        public Task PostPayrollAccrualAsync(PayrollRun run, CancellationToken ct = default) => Task.CompletedTask;
        public Task PostPayrollPaymentAsync(PayrollRun run, CancellationToken ct = default) => Task.CompletedTask;
     
        private static async Task<int> ResolveShortAsync(PosClientDbContext db, CancellationToken ct)
        {
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "541") // Cash Short (Expense)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (id == 0)
                throw new InvalidOperationException("Chart of Accounts missing code 541 (Cash Short).");
            return id;
        }

        private static async Task<int> ResolveOverAsync(PosClientDbContext db, CancellationToken ct)
        {
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "491") // Cash Over (Income)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (id == 0)
                throw new InvalidOperationException("Chart of Accounts missing code 491 (Cash Over).");

            return id;
        }

        private async Task<int> ResolveCounterTillAsync(PosClientDbContext db, int outletId, int counterId, CancellationToken ct)
        {
            return await _coa.GetCounterTillAccountIdAsync(outletId, counterId, ct);
        }

        private static async Task<int> ResolveCashInHandForOutletAsync(
            PosClientDbContext db, int outletId, CancellationToken ct)
        {
            // Get outlet code (e.g., O1, O2…)
            var outlet = await db.Outlets.AsNoTracking()
                .FirstAsync(o => o.Id == outletId, ct);
            // Per-outlet cash account code: 111-<OutletCode>  (a.k.a. CoaCode.CASH_CHILD + "-" + outlet.Code)
            var code = $"{CoaCode.CASH_CHILD}-{outlet.Code}";
            var id = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && a.OutletId == outlet.Id)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (id == 0)
                throw new InvalidOperationException(
                    $"CoA missing Cash in Hand for outlet '{outlet.Code}' (expected account code '{code}').");
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
            var chainId = session.PublicId;  // stable chain for this session
            // Use your global doc-no convention: outlet-counter-docno
            var docNo = $"{outletId}-{counterId}-TILL{session.Id:000000}";
            // Accounts
            //var tillAccId = await ResolveTillAsync(db, outletId, ct);   // per-outlet Till (memo includes counter)
                                                                       // Counter-till only
            var tillAccId = await ResolveCounterTillAsync(db, outletId, counterId, ct);
            var handAccId = await ResolveCashInHandForOutletAsync(db, outletId, ct);       // 111 Cash in Hand
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
            // 2) Over/Short so Till ends at opening float
            var diff = declaredToMove - systemCash;
            // ignore tiny rounding noise
            if (Math.Abs(diff) >= 0.005m)
            {
                if (diff > 0m)
                {
                    var overAccId = await ResolveOverAsync(db, ct);
                    rows.Add(Row(tsUtc, effDate, outletId, tillAccId, diff, 0m,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        $"Till close · Counter #{counterId} · Over (+{diff:0.##}) adj to keep float"));
                    rows.Add(Row(tsUtc, effDate, outletId, overAccId, 0m, diff,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        "Cash Over"));
                }
                else
                {
                    // SHORT: moved too little → DR Cash Short (Expense 541), CR Till
                    var abs = Math.Abs(diff);
                    var shortAccId = await ResolveShortAsync(db, ct);
                    rows.Add(Row(tsUtc, effDate, outletId, shortAccId, abs, 0m,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        "Cash Short"));
                    rows.Add(Row(tsUtc, effDate, outletId, tillAccId, 0m, abs,
                        GlDocType.TillClose, GlDocSubType.Other, session.Id, docNo, chainId, null,
                        $"Till close · Counter #{counterId} · Short (−{abs:0.##}) adj to keep float"));
                }
            }
            // Make this session's GL an “image” post: inactivate prior rows in the same chain
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
            
            var inventoryAccId = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == "1141" && !a.IsHeader && a.AllowPosting)
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (inventoryAccId == 0)
                throw new InvalidOperationException(
                    "Inventory account '1141' not found. Create it before posting Opening Stock.");
            var chainId = doc.PublicId;
            var docId = doc.Id;
            var docNo = Pos.Domain.Formatting.DocNoComposer.FromStockDoc(doc);
            var rows = new[]
            {
                // DR Inventory (11421)
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
            // keep the real time-of-day; if Ts is default, compose “today at now” in UTC
            var eff = (s.Ts != default)
                ? s.Ts
                : EffectiveTime.ComposeUtcFromDateAndNowTime(DateTime.UtcNow);
            var outletId = s.OutletId;
            var revenueId = await ResolveAccountByCodeAsync(db, "411", ct);
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct);
            var cashId = await ResolveCashAsync(db, outletId, ct);
            var cardClearingId = await ResolveSalesCardClearingForOutletAsync(db, outletId, ct);
            // Counter-till only (no outlet-till). Only resolve when TillSessionId is present.
            var tillId = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0)
                ? await ResolveCounterTillAsync(db, outletId, s.CounterId, ct)
                : 0;
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
            var total = Math.Max(0m, s.Total);                 // sale (not return)
            var tax = Math.Max(0m, s.TaxTotal);
            var net = Math.Max(0m, total - tax);
            // Raw tender (could include over-tender/change)
            var tenderCash = Math.Max(0m, s.CashAmount);
            var tenderCard = Math.Max(0m, s.CardAmount);
            // Clamp to APPLIED amounts (what we actually book)
            var appliedCard = Math.Min(tenderCard, total);
            var remaining = total - appliedCard;
            var appliedCash = Math.Min(tenderCash, remaining);
            var paidTotal = appliedCash + appliedCard;
            var arPortion = Math.Max(0m, total - paidTotal);
            // (Optional) if you want to store change on the sale for printing:
            var memo = $"Sale {Pos.Domain.Formatting.DocNoComposer.FromSale(s)}";
            // Debits: Cash/Till, CardClearing, AR (for non-walk-in balance)
            var debitRows = new List<GlEntry>();
            if (appliedCash > 0m)
            {
                var cashAccount = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0) ? tillId : cashId;
                debitRows.Add(Row(tsUtc, eff, outletId, cashAccount, debit: appliedCash, credit: 0m, GlDocType.Sale, GlDocSubType.Sale_Receipt, s, memo: memo));
            }
            if (appliedCard > 0m)
            {
                if (cardClearingId == 0)
                    throw new InvalidOperationException("Sales card clearing account is not configured (Preferences → Invoice Settings).");
                debitRows.Add(Row(tsUtc, eff, outletId, cardClearingId, debit: appliedCard, credit: 0m, GlDocType.Sale, GlDocSubType.Sale_Receipt, s, memo: memo));
            }
            if (arPortion > 0m && customerAccId.HasValue)
            {
                debitRows.Add(Row(tsUtc, eff, outletId, customerAccId.Value, debit: arPortion, credit: 0m,
                                  GlDocType.Sale, GlDocSubType.Sale_Receipt, s, partyId: s.CustomerId, memo: memo));
            }
            // Credits: Tax payable, Revenue(net)
            var creditRows = new List<GlEntry>();
            if (tax > 0m && taxPayableId != 0)
                creditRows.Add(Row(tsUtc, eff, outletId, taxPayableId, debit: 0m, credit: tax, GlDocType.Sale, GlDocSubType.Sale_Gross, s, memo: memo));
            creditRows.Add(Row(tsUtc, eff, outletId, revenueId, debit: 0m, credit: net, GlDocType.Sale, GlDocSubType.Sale_Gross, s, memo: memo));
            // ===== COGS & Inventory for this sale =====
            // Sum absolute cost of the items shipped on this sale
            var cogs = await db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.RefType == "Sale" && e.RefId == s.Id)
                .Select(e => (-e.QtyChange) * e.UnitCost) // QtyChange is negative; flip to +
                .SumAsync(ct);
            // If no stock lines or zero cost, skip gracefully
            if (cogs > 0m)
            {
                var cogsId = await ResolveAccountByCodeAsync(db, "5111", ct); // Actual cost of sold stock
                var inventoryId = await ResolveAccountByCodeAsync(db, "11431", ct);
                // DR COGS
                var eCogs = Row(tsUtc, eff, outletId, cogsId, debit: cogs, credit: 0m,
                                GlDocType.Sale, GlDocSubType.Sale_COGS, s, memo: memo);
                // CR Inventory
                var eInv = Row(tsUtc, eff, outletId, inventoryId, debit: 0m, credit: cogs,
                                GlDocType.Sale, GlDocSubType.Sale_COGS, s, memo: memo);
                debitRows.Add(eCogs);   // you can also build a separate list; merging is fine
                creditRows.Add(eInv);
            }
            var glUser = string.IsNullOrWhiteSpace(s.UpdatedBy) ? (s.CreatedBy ?? "system") : s.UpdatedBy;
            foreach (var r in debitRows.Concat(creditRows))
            {
                r.CreatedAtUtc = tsUtc;
                r.CreatedBy = glUser;
                r.UpdatedAtUtc = tsUtc;
                r.UpdatedBy = glUser;
                if (string.IsNullOrWhiteSpace(r.Memo)) r.Memo = memo;
            }
            await db.GlEntries.AddRangeAsync(debitRows.Concat(creditRows), ct);
            // do not SaveChanges here (caller controls commit)
        }

        private async Task PostSaleReturnAsync(PosClientDbContext db, Sale s, CancellationToken ct)
        {
            // A return in your system has Total < 0 (you sum ABS in Till code). We’ll work in absolutes for clarity.
            var tsUtc = DateTime.UtcNow;
            var eff = (s.Ts != default) ? s.Ts : EffectiveTime.ComposeUtcFromDateAndNowTime(DateTime.UtcNow);
            var memo = $"Sales Return {Pos.Domain.Formatting.DocNoComposer.FromSale(s)}";
            var outletId = s.OutletId;
            var salesReturnId = await ResolveAccountByCodeAsync(db, "412", ct);
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct);
            var cashId = await ResolveCashAsync(db, outletId, ct);
            var cardClearingId = await ResolveSalesCardClearingForOutletAsync(db, outletId, ct);
            // Counter-till only
            var tillId = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0)
                ? await ResolveCounterTillAsync(db, outletId, s.CounterId, ct)
                : 0;
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
            var debits = new List<GlEntry>
    {
        Row(tsUtc, eff, outletId, salesReturnId, debit: netAbs, credit: 0m, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, memo: memo)
    };
            if (taxAbs > 0m && taxPayableId != 0)
                debits.Add(Row(tsUtc, eff, outletId, taxPayableId, debit: taxAbs, credit: 0m, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, memo: memo));
            // Credits (refund to cash/till, card clearing, or reduce AR)
            var credits = new List<GlEntry>();
            if (refundCashAbs > 0m)
            {
                var cashAccount = (s.TillSessionId.HasValue && s.TillSessionId.Value > 0) ? tillId : cashId;
                credits.Add(Row(tsUtc, eff, outletId, cashAccount, debit: 0m, credit: refundCashAbs, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, memo: memo));
            }
            if (refundCardAbs > 0m)
            {
                if (cardClearingId == 0)
                    throw new InvalidOperationException("Sales card clearing account is not configured (Preferences → Invoice Settings).");
                credits.Add(Row(tsUtc, eff, outletId, cardClearingId, debit: 0m, credit: refundCardAbs, GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, memo: memo));
            }
            if (arReduceAbs > 0m && customerAccId.HasValue)
            {
                credits.Add(Row(tsUtc, eff, outletId, customerAccId.Value, debit: 0m, credit: arReduceAbs,
                                GlDocType.SaleReturn, GlDocSubType.Sale_Return, s, partyId: s.CustomerId, memo: memo));
            }
            // ===== Inventory back in, and contra-COGS for returns =====
            var invIn = await db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.RefType == "SaleReturn" && e.RefId == s.Id)
                .Select(e => (e.QtyChange) * e.UnitCost) // QtyChange is positive for return
                .SumAsync(ct);
            if (invIn > 0m)
            {
                var inventoryId = await ResolveAccountByCodeAsync(db, "11432", ct);
                var cogsReturnId = await ResolveAccountByCodeAsync(db, "5112", ct); // Actual cost of returned stock
                // DR Inventory
                debits.Add(Row(tsUtc, eff, outletId, inventoryId, debit: invIn, credit: 0m,
                               GlDocType.SaleReturn, GlDocSubType.Sale_Return_COGS, s, memo: memo));
                // CR COGS (return)
                credits.Add(Row(tsUtc, eff, outletId, cogsReturnId, debit: 0m, credit: invIn,
                                GlDocType.SaleReturn, GlDocSubType.Sale_Return_COGS, s, memo: memo));
            }
            // stamp user
            var glUser = string.IsNullOrWhiteSpace(s.UpdatedBy) ? (s.CreatedBy ?? "system") : s.UpdatedBy;
            foreach (var r in debits.Concat(credits))
            {
                r.CreatedAtUtc = tsUtc; r.CreatedBy = glUser;
                r.UpdatedAtUtc = tsUtc; r.UpdatedBy = glUser;
                if (string.IsNullOrWhiteSpace(r.Memo)) r.Memo = memo;
            }
            await db.GlEntries.AddRangeAsync(debits.Concat(credits), ct);
        }

        private async Task PostSaleDeltaAsync(Sale amended, decimal deltaSub, decimal deltaTax, bool isReturn, CancellationToken ct)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var tsUtc = DateTime.UtcNow;
            var eff = (amended.Ts != default) ? amended.Ts : EffectiveTime.ComposeUtcFromDateAndNowTime(DateTime.UtcNow);
            var memo = isReturn ? $"Sales Return Rev Δ #{amended.InvoiceNumber}" : $"Sale Rev Δ #{amended.InvoiceNumber}";
            var outletId = amended.OutletId;
            await InactivateEffectiveAsync(db, amended.PublicId, GlDocType.SaleRevision, GlDocSubType.Sale_AmendDelta, ct);
            await InactivateEffectiveAsync(db, amended.PublicId, GlDocType.SaleRevision, GlDocSubType.Sale_COGS, ct);
            var subType = isReturn ? GlDocSubType.Sale_Return : GlDocSubType.Sale_AmendDelta;
            var docType = isReturn ? GlDocType.SaleReturn : GlDocType.SaleRevision;
            var revenueId = await ResolveAccountByCodeAsync(db, isReturn ? "412" : "411", ct);
            var taxPayableId = await ResolveOptionalAccountByCodeAsync(db, "2110", ct);
            var lines = new List<GlEntry>();
            if (deltaTax != 0m && taxPayableId != 0)
            {
                lines.Add(Row(tsUtc, eff, outletId, taxPayableId,
                    debit: deltaTax < 0 ? Math.Abs(deltaTax) : 0m,
                    credit: deltaTax > 0 ? deltaTax : 0m, docType, subType, amended, memo: memo));
            }
            if (deltaSub != 0m)
            {
                var debit = 0m; var credit = 0m;
                if (isReturn)
                    debit = deltaSub > 0 ? deltaSub : 0m; // increase SalesReturns
                else
                    credit = deltaSub > 0 ? deltaSub : 0m; // increase Sales revenue

                if (deltaSub < 0 && !isReturn) debit = Math.Abs(deltaSub); // reduce revenue
                if (deltaSub < 0 && isReturn) credit = Math.Abs(deltaSub); // reduce SalesReturns

                lines.Add(Row(tsUtc, eff, outletId, revenueId, debit, credit, docType, subType, amended));
            }
            // ===== (A) Receipts / AR delta (match ΔGrand exactly) =====
            var deltaGrand = deltaSub + deltaTax;
            if (deltaGrand != 0m)
            {
                var cashId = await ResolveCashAsync(db, outletId, ct);
                var cardClearingId = await ResolveSalesCardClearingForOutletAsync(db, outletId, ct);
                // Counter-till only
                var tillId = (amended.TillSessionId.HasValue && amended.TillSessionId.Value > 0)
                    ? await ResolveCounterTillAsync(db, outletId, amended.CounterId, ct)
                    : 0;
                var cashLedger = (amended.TillSessionId.HasValue && amended.TillSessionId.Value > 0) ? tillId : cashId;
                int? customerAccId = null;
                if (amended.CustomerKind != CustomerKind.WalkIn && amended.CustomerId.HasValue)
                    customerAccId = await db.Parties.AsNoTracking()
                        .Where(p => p.Id == amended.CustomerId.Value)
                        .Select(p => p.AccountId)
                        .FirstOrDefaultAsync(ct);
                // helpers handle both signs
                void AddDebit(int accountId, decimal amount) { if (amount > 0m) lines.Add(Row(tsUtc, eff, outletId, accountId, debit: amount, credit: 0m, docType, subType, amended, partyId: amended.CustomerId, memo: memo)); }
                void AddCredit(int accountId, decimal amount) { if (amount > 0m) lines.Add(Row(tsUtc, eff, outletId, accountId, debit: 0m, credit: amount, docType, subType, amended, partyId: amended.CustomerId, memo: memo)); }
                // Raw deltas coming from UI/service
                var cashDeltaRaw = Math.Max(0m, amended.CashAmount);
                var cardDeltaRaw = Math.Max(0m, amended.CardAmount);
                // Cap applied deltas to deltaGrand (positive or negative handled below)
                var appliedCardDelta = Math.Min(cardDeltaRaw, Math.Max(0m, deltaGrand));
                var remainingDelta = Math.Max(0m, deltaGrand) - appliedCardDelta;
                var appliedCashDelta = Math.Min(cashDeltaRaw, remainingDelta);
                var cashDelta = appliedCashDelta * 1m;
                var cardDelta = appliedCardDelta * 1m;
                var collectedDelta = cashDelta + cardDelta;
                var arDelta = deltaGrand - collectedDelta; // residual to/from AR
                // Cash delta
                if (cashLedger != 0)
                {
                    if (cashDelta > 0m) AddDebit(cashLedger, cashDelta);
                    if (cashDelta < 0m) AddCredit(cashLedger, Math.Abs(cashDelta)); // refund / reduction
                }
                // Card delta
                if (cardClearingId != 0)
                {
                    if (cardDelta > 0m) AddDebit(cardClearingId, cardDelta);
                    if (cardDelta < 0m) AddCredit(cardClearingId, Math.Abs(cardDelta));
                }
                // AR delta (if a customer account exists; otherwise we leave any tiny rounding residual at zero)
                if (customerAccId.HasValue && arDelta != 0m)
                {
                    if (arDelta > 0m) AddDebit(customerAccId.Value, arDelta);
                    if (arDelta < 0m) AddCredit(customerAccId.Value, Math.Abs(arDelta));
                }
            }
            // ===== (B) COGS / Inventory delta (even when grand total = 0) =====
            var cogsDelta = await db.Set<StockEntry>().AsNoTracking()
                .Where(e => e.RefType == "SaleRev" && e.RefId == amended.Id)
                .Select(e => (-e.QtyChange) * e.UnitCost)
                .SumAsync(ct);
            if (cogsDelta != 0m)
            {
                var cogsId = await ResolveAccountByCodeAsync(db, "5111", ct);
                var inventoryId = await ResolveAccountByCodeAsync(db, "11431", ct);
                if (cogsDelta > 0m)
                {
                    lines.Add(Row(tsUtc, eff, outletId, cogsId, debit: cogsDelta, credit: 0m, docType, GlDocSubType.Sale_COGS, amended, memo: memo));
                    lines.Add(Row(tsUtc, eff, outletId, inventoryId, debit: 0m, credit: cogsDelta, docType, GlDocSubType.Sale_COGS, amended, memo: memo));
                }
                else
                {
                    var v = Math.Abs(cogsDelta);
                    // DR Inventory, CR COGS
                    lines.Add(Row(tsUtc, eff, outletId, inventoryId, debit: v, credit: 0m, docType, GlDocSubType.Sale_COGS, amended, memo: memo));
                    lines.Add(Row(tsUtc, eff, outletId, cogsId, debit: 0m, credit: v, docType, GlDocSubType.Sale_COGS, amended, memo: memo));
                }
            }
            // ✅ Apply metadata to ALL lines (after all adds)
            var glUser = string.IsNullOrWhiteSpace(amended.UpdatedBy) ? (amended.CreatedBy ?? "system") : amended.UpdatedBy;
            foreach (var r in lines)
            {
                r.CreatedAtUtc = tsUtc; r.CreatedBy = glUser;
                r.UpdatedAtUtc = tsUtc; r.UpdatedBy = glUser;
                if (string.IsNullOrWhiteSpace(r.Memo)) r.Memo = memo;
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

        // Convenience overload if the caller still has only an outletId (legacy call-sites)
        private static async Task<int> ResolveSalesCardClearingForOutletAsync(
            PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            if (!outletId.HasValue || outletId.Value <= 0)
                return 0;

            // Prefer the most recent setting among counters in this outlet
            var id = await (from s in db.InvoiceSettingsScoped.AsNoTracking()
                            join c in db.Counters.AsNoTracking() on s.OutletId equals c.Id
                            where c.OutletId == outletId.Value
                               && s.SalesCardClearingAccountId != null
                               && s.SalesCardClearingAccountId > 0
                            orderby s.UpdatedAtUtc descending
                            select s.SalesCardClearingAccountId)
                          .FirstOrDefaultAsync(ct);

            if (id.GetValueOrDefault() > 0) return id!.Value;

            // Fallback to any latest
            id = await db.InvoiceSettingsScoped.AsNoTracking()
                .Where(s => s.SalesCardClearingAccountId != null && s.SalesCardClearingAccountId > 0)
                .OrderByDescending(s => s.UpdatedAtUtc)
                .Select(s => s.SalesCardClearingAccountId)
                .FirstOrDefaultAsync(ct);

            return id ?? 0;
        }

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

        public async Task PostVoucherAsync(Voucher v, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostVoucherAsync(db, v, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task PostVoucherRevisionAsync(Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostVoucherRevisionAsync(db, newVoucher, oldLines, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task PostVoucherVoidAsync(Voucher voucherToVoid, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await PostVoucherVoidAsync(db, voucherToVoid, ct);
            await db.SaveChangesAsync(ct);
        }


        public async Task PostVoucherAsync(PosClientDbContext db, Voucher v, CancellationToken ct = default)
        {
            var tsUtc = DateTime.UtcNow;
            var effDate = (v.TsUtc == default ? DateTime.UtcNow : v.TsUtc).Date;
            var docNo = string.IsNullOrWhiteSpace(v.RefNo) ? $"VCH-{v.Id}" : v.RefNo!;
            var chainId = v.PublicId;
            var docType = v.Type switch
            {
                VoucherType.Payment => GlDocType.CashPayment,
                VoucherType.Receipt => GlDocType.CashReceipt,
                _ => GlDocType.JournalVoucher
            };
            var lines = await db.VoucherLines.AsNoTracking()
                          .Where(l => l.VoucherId == v.Id)
                          .ToListAsync(ct);
            if (lines.Count == 0) return;
            if (docType == GlDocType.JournalVoucher)
            {
                await db.GlEntries.Where(g => g.ChainId == chainId && g.IsEffective && g.DocType == GlDocType.JournalVoucher)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);
                var rows = new List<GlEntry>(lines.Count);
                foreach (var ln in lines)
                {
                    if (ln.Debit > 0m) rows.Add(Row(tsUtc, effDate, v.OutletId, ln.AccountId, ln.Debit, 0m, GlDocType.JournalVoucher, GlDocSubType.Other, v.Id, docNo, chainId, null, ln.Description ?? v.Memo));
                    if (ln.Credit > 0m) rows.Add(Row(tsUtc, effDate, v.OutletId, ln.AccountId, 0m, ln.Credit, GlDocType.JournalVoucher, GlDocSubType.Other, v.Id, docNo, chainId, null, ln.Description ?? v.Memo));
                }
                if (rows.Count > 0) await db.GlEntries.AddRangeAsync(rows, ct);
                return;
            }

            if (!v.OutletId.HasValue || v.OutletId.Value == 0)
                throw new InvalidOperationException("Outlet is required for Cash vouchers.");
            var outletId = v.OutletId!.Value;
            var cashAccId = await ResolveCashInHandForOutletAsync(db, outletId, ct);
            await db.GlEntries.Where(g => g.ChainId == chainId && g.IsEffective && g.DocType == docType)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);
            var list = new List<GlEntry>();
            if (docType == GlDocType.CashPayment)
            {
                var total = lines.Sum(l => Math.Max(0m, l.Debit));
                foreach (var ln in lines.Where(l => l.Debit > 0m))
                    list.Add(Row(tsUtc, effDate, outletId, ln.AccountId, ln.Debit, 0m, GlDocType.CashPayment, GlDocSubType.Other, v.Id, docNo, chainId, null, ln.Description ?? v.Memo));
                if (total > 0m)
                    list.Add(Row(tsUtc, effDate, outletId, cashAccId, 0m, total, GlDocType.CashPayment, GlDocSubType.Other, v.Id, docNo, chainId, null, v.Memo));
            }
            else // CashReceipt
            {
                var total = lines.Sum(l => Math.Max(0m, l.Credit));
                if (total > 0m)
                    list.Add(Row(tsUtc, effDate, outletId, cashAccId, total, 0m, GlDocType.CashReceipt, GlDocSubType.Other, v.Id, docNo, chainId, null, v.Memo));
                foreach (var ln in lines.Where(l => l.Credit > 0m))
                    list.Add(Row(tsUtc, effDate, outletId, ln.AccountId, 0m, ln.Credit, GlDocType.CashReceipt, GlDocSubType.Other, v.Id, docNo, chainId, null, ln.Description ?? v.Memo));
            }
            if (list.Count > 0) await db.GlEntries.AddRangeAsync(list, ct);
        }

        public async Task PostVoucherRevisionAsync(PosClientDbContext db, Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default)
        {
            var tsUtc = DateTime.UtcNow;
            var effDate = (newVoucher.TsUtc == default ? DateTime.UtcNow : newVoucher.TsUtc).Date;
            var docNo = string.IsNullOrWhiteSpace(newVoucher.RefNo) ? $"VCH-{newVoucher.Id}" : newVoucher.RefNo!;
            var chainId = newVoucher.PublicId;
            var docType = newVoucher.Type switch
            {
                VoucherType.Payment => GlDocType.CashPayment,
                VoucherType.Receipt => GlDocType.CashReceipt,
                _ => GlDocType.JournalVoucher
            };
            var newLines = await db.VoucherLines.AsNoTracking()
                              .Where(l => l.VoucherId == newVoucher.Id)
                              .ToListAsync(ct);
            var map = new Dictionary<int, (decimal dr, decimal cr)>();
            void A(int acc, decimal dr, decimal cr)
            { if (!map.TryGetValue(acc, out var cur)) cur = (0m, 0m); map[acc] = (cur.dr + dr, cur.cr + cr); }
            foreach (var o in oldLines) { if (o.Debit > 0m) A(o.AccountId, -Math.Round(o.Debit, 2), 0m); if (o.Credit > 0m) A(o.AccountId, 0m, -Math.Round(o.Credit, 2)); }
            foreach (var n in newLines) { if (n.Debit > 0m) A(n.AccountId, Math.Round(n.Debit, 2), 0m); if (n.Credit > 0m) A(n.AccountId, 0m, Math.Round(n.Credit, 2)); }
            var rows = new List<GlEntry>();
            if (docType == GlDocType.JournalVoucher)
            {
                foreach (var (acc, (dr, cr)) in map)
                {
                    if (dr > 0m) rows.Add(Row(tsUtc, effDate, newVoucher.OutletId, acc, dr, 0m, GlDocType.JournalVoucher, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                    if (cr > 0m) rows.Add(Row(tsUtc, effDate, newVoucher.OutletId, acc, 0m, cr, GlDocType.JournalVoucher, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                }
            }
            else
            {
                if (!newVoucher.OutletId.HasValue || newVoucher.OutletId.Value == 0)
                    throw new InvalidOperationException("Outlet is required for Cash vouchers.");
                var outletId = newVoucher.OutletId!.Value;
                var cashAccId = await ResolveCashInHandForOutletAsync(db, outletId, ct);
                decimal deltaDrNonCash = 0m, deltaCrNonCash = 0m;
                foreach (var (acc, (dr, cr)) in map)
                {
                    if (acc == cashAccId) continue;
                    if (docType == GlDocType.CashPayment)
                    {
                        if (dr > 0m) rows.Add(Row(tsUtc, effDate, outletId, acc, dr, 0m, GlDocType.CashPayment, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                        if (cr > 0m) rows.Add(Row(tsUtc, effDate, outletId, acc, 0m, cr, GlDocType.CashPayment, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                        deltaDrNonCash += dr; deltaCrNonCash += cr;
                    }
                    else // CashReceipt
                    {
                        if (dr > 0m) rows.Add(Row(tsUtc, effDate, outletId, acc, dr, 0m, GlDocType.CashReceipt, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                        if (cr > 0m) rows.Add(Row(tsUtc, effDate, outletId, acc, 0m, cr, GlDocType.CashReceipt, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                        deltaDrNonCash += dr; deltaCrNonCash += cr;
                    }
                }
                var net = (docType == GlDocType.CashPayment) ? (deltaDrNonCash - deltaCrNonCash)
                                                             : (deltaCrNonCash - deltaDrNonCash);
                if (net != 0m)
                {
                    if (docType == GlDocType.CashPayment)
                        rows.Add(net > 0m
                            ? Row(tsUtc, effDate, outletId, cashAccId, 0m, Math.Abs(net), GlDocType.CashPayment, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo)
                            : Row(tsUtc, effDate, outletId, cashAccId, Math.Abs(net), 0m, GlDocType.CashPayment, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                    else
                        rows.Add(net > 0m
                            ? Row(tsUtc, effDate, outletId, cashAccId, Math.Abs(net), 0m, GlDocType.CashReceipt, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo)
                            : Row(tsUtc, effDate, outletId, cashAccId, 0m, Math.Abs(net), GlDocType.CashReceipt, GlDocSubType.Voucher_AmendDelta, newVoucher.Id, docNo, chainId, null, newVoucher.Memo));
                }
            }
            if (rows.Count > 0) await db.GlEntries.AddRangeAsync(rows, ct);
        }

        public async Task PostVoucherVoidAsync(PosClientDbContext db, Voucher voucherToVoid, CancellationToken ct = default)
        {
            await db.GlEntries
                .Where(g => g.ChainId == voucherToVoid.PublicId && g.IsEffective)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEffective, false), ct);
        }
    }
}