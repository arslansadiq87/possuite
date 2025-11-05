using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IGlPostingService
    {
        Task PostSaleAsync(Sale sale);
        Task PostSaleReturnAsync(Sale sale);
        Task PostPurchaseAsync(Purchase p);
        Task PostPurchaseReturnAsync(Purchase p);
        Task PostVoucherAsync(Voucher v);
        Task PostVoucherAsync(PosClientDbContext db, Voucher v);
        Task PostVoucherVoidAsync(PosClientDbContext db, Voucher voucherToVoid);
        Task PostVoucherRevisionAsync(PosClientDbContext db, Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines);
        Task PostPayrollAccrualAsync(PayrollRun run);   // Dr Salaries Expense, Cr Salaries Payable
        Task PostPayrollPaymentAsync(PayrollRun run);   // Dr Salaries Payable, Cr Cash/Bank
        Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax);
        Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax);
        Task PostTillCloseAsync(TillSession session, decimal declaredCash, decimal systemCash);
    }

    public sealed class GlPostingService : IGlPostingService
    {
        private readonly PosClientDbContext _db;
        private readonly ICoaService _coa;
        private readonly IInvoiceSettingsService _invSettings;   // NEW

        public GlPostingService(PosClientDbContext db, ICoaService coa, IInvoiceSettingsService invSettings)
        {
            _db = db;
            _coa = coa;
            _invSettings = invSettings;    // NEW
        }

        // Aligned with CoATemplateSeeder
        private const string BANK = "113";    // Bank Accounts
        private const string INV = "1140";   // Inventory on hand (leaf we added)
        private const string AR = "6200";   // Customers (control) - Parties
        private const string AP = "6100";   // Suppliers (control) - Parties
        private const string TAX = "2110";   // Sales tax payable (output)
        private const string SALP = "2111";   // Salaries payable
        private const string SALES = "411";    // Gross sales value
        private const string COGS = "5111";   // Actual cost of sold stock
        private const string SALX = "52011";  // Wages (Payroll)
        // ----- helpers -------------------------------------------------------
        private async Task<int> IdOfAsync(string code) =>
            (await _db.Accounts.AsNoTracking().FirstAsync(a => a.Code == code)).Id;
        private async Task LineAsync(DateTime tsUtc, int? outletId, string code, decimal dr, decimal cr, GlDocType dt, int docId, string? memo = null)
        {
            var id = await IdOfAsync(code);
            _db.GlEntries.Add(new GlEntry
            {
                TsUtc = tsUtc,
                OutletId = outletId,
                AccountId = id,
                Debit = dr,
                Credit = cr,
                DocType = dt,
                DocId = docId,
                Memo = memo
            });
        }
        // NEW: when we already have the AccountId (Cash/Till via CoaService)
        private void LineById(DateTime tsUtc, int? outletId, int accountId, decimal dr, decimal cr, GlDocType dt, int docId, string? memo = null)
        {
            _db.GlEntries.Add(new GlEntry
            {
                TsUtc = tsUtc,
                OutletId = outletId,
                AccountId = accountId,
                Debit = dr,
                Credit = cr,
                DocType = dt,
                DocId = docId,
                Memo = memo
            });
        }

        // ----- Sales ---------------------------------------------------------
        public async Task PostSaleAsync(Sale sale)
        {
            var ts = sale.Ts;
            var outletId = sale.OutletId;
            decimal paidNow = sale.CashAmount + sale.CardAmount;
            decimal tax = sale.TaxTotal;
            decimal revenuePortion = sale.Total - tax;
            decimal credit = sale.Total - paidNow;
            // DR: Cash (Till) for cash portion; DR Bank (or Undeposited) for card portion
            if (sale.CashAmount > 0m)
            {
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId);
                LineById(ts, outletId, tillAccId, sale.CashAmount, 0m, GlDocType.Sale, sale.Id, "Sale cash to Till");
            }
            if (sale.CardAmount > 0m)
            {
                // Block if per-outlet card clearing not configured
                var (s, _) = await _invSettings.GetAsync(outletId, "en");
                if (s?.SalesCardClearingAccountId is null)
                    throw new InvalidOperationException("Cannot take CARD payments: configure a Sales Card Clearing Account in Invoice Settings for this outlet.");
                var bankId = s!.SalesCardClearingAccountId.Value;
                LineById(ts, outletId, bankId, sale.CardAmount, 0m, GlDocType.Sale, sale.Id, "Sale card receipt");
            }
            if (credit > 0m)
                await LineAsync(ts, outletId, AR, credit, 0m, GlDocType.Sale, sale.Id, "Sale on credit");
            // CR: Revenue and Output Tax
            if (revenuePortion != 0m)
                await LineAsync(ts, outletId, SALES, 0m, revenuePortion, GlDocType.Sale, sale.Id, "Sales revenue");
            if (tax > 0m)
                await LineAsync(ts, outletId, TAX, 0m, tax, GlDocType.Sale, sale.Id, "Output tax");
            // COGS from stock ledger
            var cost = await _db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "Sale" && se.RefId == sale.Id && se.QtyChange < 0m)
                .SumAsync(se => (-se.QtyChange) * se.UnitCost);
            if (cost != 0m)
            {
                await LineAsync(ts, outletId, COGS, cost, 0m, GlDocType.Sale, sale.Id, "COGS");
                await LineAsync(ts, outletId, INV, 0m, cost, GlDocType.Sale, sale.Id, "Inventory out");
            }
            await _db.SaveChangesAsync();
        }

        public async Task PostSaleReturnAsync(Sale sale)
        {
            var ts = sale.Ts;
            var outletId = sale.OutletId;
            decimal paidBack = sale.CashAmount + sale.CardAmount;
            decimal tax = sale.TaxTotal;
            decimal revenuePortion = sale.Total - tax;   // usually negative for returns
            decimal creditReversed = sale.Total - paidBack;
            // If you refund from the counter, pull from Till; card part goes to BANK
            if (sale.CashAmount > 0m)
            {
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId);
                LineById(ts, outletId, tillAccId, 0m, sale.CashAmount, GlDocType.SaleReturn, sale.Id, "Refund from Till");
            }
            if (sale.CardAmount > 0m)
            {
                await LineAsync(ts, outletId, BANK, 0m, sale.CardAmount, GlDocType.SaleReturn, sale.Id, "Refund via bank/card");
            }
            if (creditReversed > 0m)
                await LineAsync(ts, outletId, AR, 0m, creditReversed, GlDocType.SaleReturn, sale.Id, "Reverse AR");
            // Reverse revenue and output tax
            if (revenuePortion != 0m)
                await LineAsync(ts, outletId, SALES, revenuePortion, 0m, GlDocType.SaleReturn, sale.Id, "Reverse revenue");
            if (tax > 0m)
                await LineAsync(ts, outletId, TAX, tax, 0m, GlDocType.SaleReturn, sale.Id, "Reverse output tax");
            // Inventory back
            var cost = await _db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "SaleReturn" && se.RefId == sale.Id && se.QtyChange > 0m)
                .SumAsync(se => (se.QtyChange) * se.UnitCost);
            if (cost != 0m)
            {
                await LineAsync(ts, outletId, COGS, 0m, cost, GlDocType.SaleReturn, sale.Id, "Reverse COGS");
                await LineAsync(ts, outletId, INV, cost, 0m, GlDocType.SaleReturn, sale.Id, "Inventory in");
            }
            await _db.SaveChangesAsync();
        }

        // ----- Purchases -----------------------------------------------------
        public async Task PostPurchaseAsync(Purchase p)
        {
            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;

            // ACCOUNTING CHOICE: Capitalize tax into inventory (gross) to avoid a separate Input-Tax account.
            // If you later add an Input Tax asset (e.g., 1160), split grand total into goods+tax and DR that asset instead.
            var gross = p.GrandTotal;

            // DR Inventory (gross), CR Accounts Payable (gross)
            if (gross != 0m)
            {
                await LineAsync(ts, outletId, INV, gross, 0m, GlDocType.Purchase, p.Id, $"PO #{p.DocNo} · Inventory in (gross)");
                await LineAsync(ts, outletId, AP, 0m, gross, GlDocType.Purchase, p.Id, $"PO #{p.DocNo} · To AP (gross)");
            }

            // IMPORTANT: Do NOT post any cash/bank here.
            // All settlements must go through PurchasesService.AddPaymentAsync(...).

            await _db.SaveChangesAsync();
        }


        public async Task PostPurchaseReturnAsync(Purchase p)
        {
            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;

            var gross = p.GrandTotal;

            // Reverse the above: CR Inventory (gross), DR Accounts Payable (gross)
            if (gross != 0m)
            {
                await LineAsync(ts, outletId, INV, 0m, gross, GlDocType.PurchaseReturn, p.Id, $"PR #{p.DocNo} · Inventory out (gross)");
                await LineAsync(ts, outletId, AP, gross, 0m, GlDocType.PurchaseReturn, p.Id, $"PR #{p.DocNo} · Reduce AP (gross)");
            }

            // IMPORTANT: Do NOT post refund cash/bank here.
            // If supplier refunds immediately, record it via AddPaymentAsync(...).

            await _db.SaveChangesAsync();
        }

        // ----- Vouchers ------------------------------------------------------
        public async Task PostVoucherAsync(PosClientDbContext db, Voucher v)
        {
            // Load lines once using the shared context
            await db.Entry(v).Collection(x => x.Lines).LoadAsync();
            var outletId = v.OutletId;
            var ts = v.TsUtc;
            // Helper to add a line by account Id on the shared context
            void LineByIdLocal(int accountId, decimal dr, decimal cr, GlDocType dt, string? memo = null)
            {
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = accountId,
                    Debit = dr,
                    Credit = cr,
                    DocType = dt,
                    DocId = v.Id,
                    Memo = memo
                });
            }
            // 1) Push user-entered lines as-is
            decimal totalDr = 0m, totalCr = 0m;
            foreach (var ln in v.Lines)
            {
                totalDr += ln.Debit;
                totalCr += ln.Credit;
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = ln.AccountId,
                    Debit = ln.Debit,
                    Credit = ln.Credit,
                    DocType = v.Type == VoucherType.Journal ? GlDocType.JournalVoucher
            : v.Type == VoucherType.Debit ? GlDocType.CashPayment
            : GlDocType.CashReceipt,
                    DocId = v.Id,
                    Memo = ln.Description ?? v.Memo
                });
            }
            // 2) Auto cash side for Debit/Credit vouchers (per-outlet cash in hand)
            if (v.Type == VoucherType.Debit)
            {
                if (totalDr <= 0m) throw new InvalidOperationException("Debit Voucher must have Debit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(db, outletId);
                LineByIdLocal(cashId, 0m, totalDr, GlDocType.CashPayment, "Cash payment (auto)");
            }
            else if (v.Type == VoucherType.Credit)
            {
                if (totalCr <= 0m) throw new InvalidOperationException("Credit Voucher must have Credit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(db, outletId);
                LineByIdLocal(cashId, totalCr, 0m, GlDocType.CashReceipt, "Cash receipt (auto)");
            }
            else
            {
                if (Math.Abs(totalDr - totalCr) > 0.004m)
                    throw new InvalidOperationException("Journal Voucher must balance.");
            }
            // Persist GL entries using the SAME DbContext/transaction
            await db.SaveChangesAsync();
        }
        
        public async Task PostVoucherAsync(Voucher v)
        {
            // 🔒 Guard: cash vouchers must have an outlet (cash account is per-outlet)
            if (v.Type != VoucherType.Journal && v.OutletId == null)
            {
                throw new InvalidOperationException(
                    "Debit/Credit voucher requires OutletId to resolve Cash in Hand account.");
            }
            await _db.Entry(v).Collection(x => x.Lines).LoadAsync();
            var outletId = v.OutletId;
            var ts = v.TsUtc;
            // Helper to add a line by account Id
            void LineByIdLocal(int accountId, decimal dr, decimal cr, GlDocType dt, string? memo = null)
            {
                _db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = accountId,
                    Debit = dr,
                    Credit = cr,
                    DocType = dt,
                    DocId = v.Id,
                    Memo = memo
                });
            }
            var totalDr = v.Lines.Sum(l => l.Debit);
            var totalCr = v.Lines.Sum(l => l.Credit);
            // 1) Always post user lines exactly as entered
            foreach (var ln in v.Lines)
            {
                _db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = ln.AccountId,
                    Debit = ln.Debit,
                    Credit = ln.Credit,
                    DocType = v.Type == VoucherType.Journal ? GlDocType.JournalVoucher
                            : v.Type == VoucherType.Debit ? GlDocType.CashPayment
                            : GlDocType.CashReceipt,
                    DocId = v.Id,
                    Memo = ln.Description ?? v.Memo
                });
            }
            // 2) Auto cash side for Debit/Credit vouchers (Cash in Hand per-outlet)
            if (v.Type == VoucherType.Debit)
            {
                if (totalDr <= 0m) throw new InvalidOperationException("Debit Voucher must have Debit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(outletId);
                LineByIdLocal(cashId, 0m, totalDr, GlDocType.CashPayment, "Cash payment (auto)");
            }
            else if (v.Type == VoucherType.Credit)
            {
                if (totalCr <= 0m) throw new InvalidOperationException("Credit Voucher must have Credit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(outletId);
                LineByIdLocal(cashId, totalCr, 0m, GlDocType.CashReceipt, "Cash receipt (auto)");
            }
            else
            {
                if (Math.Abs(totalDr - totalCr) > 0.004m)
                    throw new InvalidOperationException("Journal Voucher must balance.");
            }
            await _db.SaveChangesAsync();
        }

    public async Task PostVoucherVoidAsync(PosClientDbContext db, Voucher voucherToVoid)
    {
        // We tagged voucher user-lines as Journal/CashPayment/CashReceipt in your PostVoucherAsync.
        var baseDocTypes = new[] { GlDocType.JournalVoucher, GlDocType.CashPayment, GlDocType.CashReceipt };
        var lines = await db.GlEntries
            .Where(g => g.DocId == voucherToVoid.Id && baseDocTypes.Contains(g.DocType))
            .AsNoTracking()
            .ToListAsync();
        if (lines.Count == 0) return;
        var ts = DateTime.UtcNow;
        foreach (var g in lines)
        {
            db.GlEntries.Add(new GlEntry
            {
                TsUtc = ts,
                OutletId = g.OutletId,
                AccountId = g.AccountId,
                Debit = g.Credit,         // swap
                Credit = g.Debit,         // swap
                DocType = GlDocType.VoucherVoid,
                DocId = voucherToVoid.Id,
                Memo = $"VOID of voucher #{voucherToVoid.Id}"
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task PostVoucherRevisionAsync(PosClientDbContext db, Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines)
    {
        // Load NEW lines (ensure attached)
        await db.Entry(newVoucher).Collection(x => x.Lines).LoadAsync();
        // Aggregate by AccountId (old vs new)
        var oldByAcc = oldLines.GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => (dr: g.Sum(x => x.Debit), cr: g.Sum(x => x.Credit)));
        var newByAcc = newVoucher.Lines.GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => (dr: g.Sum(x => x.Debit), cr: g.Sum(x => x.Credit)));
        var allAccs = oldByAcc.Keys.Union(newByAcc.Keys);
        var ts = DateTime.UtcNow;
        var outletId = newVoucher.OutletId;
        decimal deltaDrTotal = 0m, deltaCrTotal = 0m;
        foreach (var accId in allAccs)
        {
            oldByAcc.TryGetValue(accId, out var o);
            newByAcc.TryGetValue(accId, out var n);
            var deltaDr = n.dr - o.dr;
            var deltaCr = n.cr - o.cr;
            if (Math.Abs(deltaDr) < 0.0005m && Math.Abs(deltaCr) < 0.0005m) continue;
            deltaDrTotal += deltaDr;
            deltaCrTotal += deltaCr;
            db.GlEntries.Add(new GlEntry
            {
                TsUtc = ts,
                OutletId = outletId,
                AccountId = accId,
                Debit = Math.Max(0, deltaDr),
                Credit = Math.Max(0, deltaCr),
                DocType = GlDocType.VoucherRevision,
                DocId = newVoucher.Id,
                Memo = $"Revision delta vs #{newVoucher.AmendedFromId}"
            });
        }
        // Handle auto cash line deltas for Debit/Credit vouchers
        if (newVoucher.Type == VoucherType.Debit || newVoucher.Type == VoucherType.Credit)
        {
            // For Credit voucher: cash was auto-debited by totalCr; apply delta accordingly
            var cashId = await _coa.GetCashAccountIdAsync(db, newVoucher.OutletId);
            if (newVoucher.Type == VoucherType.Debit && Math.Abs(deltaDrTotal) > 0.0005m)
            {
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = cashId,
                    Debit = 0m,
                    Credit = Math.Max(0, deltaDrTotal),
                    DocType = GlDocType.VoucherRevision,
                    DocId = newVoucher.Id,
                    Memo = "Auto cash delta (Debit voucher)"
                });
            }
            else if (newVoucher.Type == VoucherType.Credit && Math.Abs(deltaCrTotal) > 0.0005m)
            {
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = outletId,
                    AccountId = cashId,
                    Debit = Math.Max(0, deltaCrTotal),
                    Credit = 0m,
                    DocType = GlDocType.VoucherRevision,
                    DocId = newVoucher.Id,
                    Memo = "Auto cash delta (Credit voucher)"
                });
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task PostPayrollAccrualAsync(PayrollRun run)
        {
            // Dr Salaries Expense (5130), Cr Salaries Payable (2200)
            var ts = run.CreatedAtUtc;
            var total = run.TotalNet;
            if (total != 0m)
            {
                await LineAsync(ts, null, SALX, total, 0, GlDocType.PayrollAccrual, run.Id, "Payroll accrual");
                await LineAsync(ts, null, SALP, 0, total, GlDocType.PayrollAccrual, run.Id, "Salaries payable");
                await _db.SaveChangesAsync();
            }
        }

        public async Task PostPayrollPaymentAsync(PayrollRun run)
        {
            // Dr Salaries Payable (2200), Cr Cash in Hand (company/outlet as you prefer)
            var ts = DateTime.UtcNow;
            var total = run.TotalNet;
            if (total != 0m)
            {
                await LineAsync(ts, null, SALP, total, 0, GlDocType.PayrollPayment, run.Id, "Clear payable");
                // If payroll is paid at a specific outlet, swap null with outletId and use Cash in Hand (outlet)
                var handAccId = await _coa.GetCashAccountIdAsync(outletId: null); // company-scope by default
                LineById(ts, null, handAccId, 0, total, GlDocType.PayrollPayment, run.Id, "Payout (Cash)");
                await _db.SaveChangesAsync();
            }
        }

        // ----- Revisions -----------------------------------------------------
        public async Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax)
        {
            var ts = newSale.UpdatedAtUtc ?? newSale.Ts;
            var outletId = newSale.OutletId;
            var deltaGrand = deltaSub + deltaTax;
            if (deltaGrand <= 0.005m) return;
            // Cash part of amendment collected at counter => Till
            var tillAccId = await _coa.GetTillAccountIdAsync(outletId);
            LineById(ts, outletId, tillAccId, deltaGrand, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: collect difference (Till)");
            if (deltaSub > 0) await LineAsync(ts, outletId, SALES, 0, deltaSub, GlDocType.SaleRevision, newSale.Id, "Amendment: revenue");
            if (deltaTax > 0) await LineAsync(ts, outletId, TAX, 0, deltaTax, GlDocType.SaleRevision, newSale.Id, "Amendment: tax");
            var se = await _db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "SaleRev" && x.RefId == newSale.Id)
                .ToListAsync();
            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0)
            {
                await LineAsync(ts, outletId, COGS, costOut, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: COGS");
                await LineAsync(ts, outletId, INV, 0, costOut, GlDocType.SaleRevision, newSale.Id, "Amendment: inventory out");
            }
            var costIn = se.Where(x => x.QtyChange > 0).Sum(x => (x.QtyChange) * x.UnitCost);
            if (costIn > 0)
            {
                await LineAsync(ts, outletId, COGS, 0, costIn, GlDocType.SaleRevision, newSale.Id, "Amendment: reverse COGS");
                await LineAsync(ts, outletId, INV, costIn, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: inventory in");
            }
            await _db.SaveChangesAsync();
        }

        public async Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax)
        {
            var ts = amended.UpdatedAtUtc ?? amended.Ts;
            var outletId = amended.OutletId;
            var deltaGrand = deltaSub + deltaTax;
            if (deltaGrand < -0.005m)
            {
                // extra refund now -> credit Till
                if (Math.Abs(deltaSub) > 0.005m)
                    await LineAsync(ts, outletId, SALES, Math.Abs(deltaSub), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase revenue reversal");
                if (Math.Abs(deltaTax) > 0.005m)
                    await LineAsync(ts, outletId, TAX, Math.Abs(deltaTax), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase tax reversal");
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId);
                LineById(ts, outletId, tillAccId, 0, Math.Abs(deltaGrand), GlDocType.SaleReturnRevision, amended.Id, "Return amend: extra refund (Till)");
            }
            else if (deltaGrand > 0.005m)
            {
                // customer returns some refund -> debit Till
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId);
                LineById(ts, outletId, tillAccId, deltaGrand, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: collect back (Till)");
                if (Math.Abs(deltaSub) > 0.005m)
                    await LineAsync(ts, outletId, SALES, 0, Math.Abs(deltaSub), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce revenue reversal");
                if (Math.Abs(deltaTax) > 0.005m)
                    await LineAsync(ts, outletId, TAX, 0, Math.Abs(deltaTax), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce tax reversal");
            }
            // Stock deltas
            var se = await _db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "Amend" && x.RefId == amended.Id)
                .ToListAsync();
            var costIn = se.Where(x => x.QtyChange > 0).Sum(x => x.QtyChange * x.UnitCost);
            if (costIn > 0.005m)
            {
                await LineAsync(ts, outletId, INV, costIn, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory in");
                await LineAsync(ts, outletId, COGS, 0, costIn, GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce COGS");
            }
            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0.005m)
            {
                await LineAsync(ts, outletId, COGS, costOut, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase COGS");
                await LineAsync(ts, outletId, INV, 0, costOut, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory out");
            }
            await _db.SaveChangesAsync();
        }
        // ----- Till Close (Z) ------------------------------------------------
        public async Task PostTillCloseAsync(TillSession session, decimal declaredCash, decimal systemCash)
        {
            var ts = session.CloseTs ?? DateTime.UtcNow;
            var outletId = session.OutletId;
            var tillId = await _coa.GetTillAccountIdAsync(outletId);
            var handId = await _coa.GetCashAccountIdAsync(outletId);
            var move = declaredCash;                  // physically moved to Cash in Hand
            var overShort = declaredCash - systemCash;
            // Move: DR Cash in Hand, CR Cash in Till
            if (move != 0m)
            {
                LineById(ts, outletId, handId, move, 0m, GlDocType.TillClose, session.Id, "Till close: move declared cash to Hand");
                LineById(ts, outletId, tillId, 0m, move, GlDocType.TillClose, session.Id, "Till close");
            }
            // Over/Short
            if (Math.Abs(overShort) > 0.005m)
            {
                if (overShort > 0) // overage → credit income (use your preferred income account code)
                {
                    // using "49" Other incomes if you have it; adjust as needed:
                    var otherIncomeId = await IdOfAsync("49");
                    LineById(ts, outletId, otherIncomeId, 0m, overShort, GlDocType.TillClose, session.Id, "Cash over");
                }
                else // shortage → debit expense (your template: 541 Cash short)
                {
                    var cashShortId = await IdOfAsync("541");
                    LineById(ts, outletId, cashShortId, -overShort, 0m, GlDocType.TillClose, session.Id, "Cash short");
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}
