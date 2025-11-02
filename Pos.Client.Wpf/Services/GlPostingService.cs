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
        Task PostPayrollAccrualAsync(PayrollRun run);   // Dr Salaries Expense, Cr Salaries Payable
        Task PostPayrollPaymentAsync(PayrollRun run);   // Dr Salaries Payable, Cr Cash/Bank
        Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax);
        Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax);

        // NEW: Move declared cash from Till -> Cash in Hand and post over/short
        Task PostTillCloseAsync(TillSession session, decimal declaredCash, decimal systemCash);
    }

    public sealed class GlPostingService : IGlPostingService
    {
        private readonly PosClientDbContext _db;
        private readonly ICoaService _coa;

        public GlPostingService(PosClientDbContext db, ICoaService coa)
        {
            _db = db;
            _coa = coa;
        }

        // Keep your existing codes for non-cash accounts (safe with your current seeding)
        private const string BANK = "1010";
        private const string INV = "1100";
        private const string AR = "1200";
        private const string AP = "2000";
        private const string TAX = "2100";
        private const string SALP = "2200";
        private const string SALES = "4000";
        private const string COGS = "5000";
        private const string SALX = "5130"; // Salaries Expense

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
                // keep your existing bank path (adjust BANK code to your card clearing account if needed)
                await LineAsync(ts, outletId, BANK, sale.CardAmount, 0m, GlDocType.Sale, sale.Id, "Sale card/bank");
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

            var goodsValue = p.GrandTotal - p.Tax;
            if (goodsValue != 0m)
                await LineAsync(ts, outletId, INV, goodsValue, 0m, GlDocType.Purchase, p.Id, "Inventory in");

            var paid = p.CashPaid;
            var due = p.GrandTotal - paid;

            // IMPORTANT: purchases "paid now" usually come from Cash in Hand (not Till)
            if (paid > 0m)
            {
                var handAccId = await _coa.GetCashAccountIdAsync(outletId);
                LineById(ts, outletId, handAccId, 0m, paid, GlDocType.Purchase, p.Id, "Paid now (from Cash in Hand)");
            }

            if (due > 0m)
                await LineAsync(ts, outletId, AP, 0m, due, GlDocType.Purchase, p.Id, "AP created");

            // If you treat VAT as input-tax asset: add here to your input-tax account.

            await _db.SaveChangesAsync();
        }

        public async Task PostPurchaseReturnAsync(Purchase p)
        {
            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;

            var goodsValue = p.GrandTotal - p.Tax;
            if (goodsValue != 0m)
                await LineAsync(ts, outletId, INV, 0m, goodsValue, GlDocType.PurchaseReturn, p.Id, "Inventory out");

            var paid = p.CashPaid;
            var due = p.GrandTotal - paid;

            // Refund received comes into Cash in Hand
            if (paid > 0m)
            {
                var handAccId = await _coa.GetCashAccountIdAsync(outletId);
                LineById(ts, outletId, handAccId, paid, 0m, GlDocType.PurchaseReturn, p.Id, "Refund received (to Cash in Hand)");
            }

            if (due > 0m)
                await LineAsync(ts, outletId, AP, due, 0m, GlDocType.PurchaseReturn, p.Id, "Reduce AP");

            await _db.SaveChangesAsync();
        }

        // ----- Vouchers ------------------------------------------------------

        public async Task PostVoucherAsync(Voucher v)
        {
            // Leave as-is: UI controls which accounts to hit (receipt/payment)
            // Tip: default the "cash" picker in UI to CoaService.GetCashAccountIdAsync(v.OutletId)
            foreach (var ln in v.Lines)
            {
                _db.GlEntries.Add(new GlEntry
                {
                    TsUtc = v.TsUtc,
                    OutletId = v.OutletId,
                    AccountId = ln.AccountId,
                    Debit = ln.Debit,
                    Credit = ln.Credit,
                    DocType = GlDocType.JournalVoucher,
                    DocId = v.Id,
                    Memo = v.Memo
                });
            }
            await _db.SaveChangesAsync();
        }

        // ----- Payroll -------------------------------------------------------

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
