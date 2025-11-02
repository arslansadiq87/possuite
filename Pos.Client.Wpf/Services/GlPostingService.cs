using System;
using System.Linq;
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
        Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax); // <-- ADD THIS
        Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax);

    }

    public sealed class GlPostingService : IGlPostingService
    {
        private readonly PosClientDbContext _db;
        public GlPostingService(PosClientDbContext db) => _db = db;

        private const string CASH = "1000";
        private const string BANK = "1010";
        private const string INV = "1100";
        private const string AR = "1200";
        private const string AP = "2000";
        private const string TAX = "2100";
        private const string SALP = "2200";
        private const string SALES = "4000";
        private const string COGS = "5000";
        private const string SALX = "5130"; // Salaries Expense

        private async Task<int> IdOfAsync(string code) =>
            (await _db.Accounts.AsNoTracking().FirstAsync(a => a.Code == code)).Id;

        public async Task<int> PostAsync(DateTime tsUtc, int? outletId,
            string refType, int? refId, string? memo,
            IEnumerable<(int accountId, decimal debit, decimal credit, int? partyId, string? lineMemo)> lines,
            CancellationToken ct = default)
        {
            var j = new Journal
            {
                TsUtc = tsUtc,
                OutletId = outletId,
                RefType = refType,
                RefId = refId,
                Memo = memo
            };
            _db.Journals.Add(j);
            await _db.SaveChangesAsync(ct);

            decimal dr = 0, cr = 0;
            foreach (var l in lines)
            {
                dr += l.debit; cr += l.credit;
                _db.JournalLines.Add(new JournalLine
                {
                    JournalId = j.Id,
                    AccountId = l.accountId,
                    PartyId = l.partyId,
                    Debit = l.debit,
                    Credit = l.credit,
                    Memo = l.lineMemo
                });

                // Also reflect in PartyLedger if attached to a party
                if (l.partyId.HasValue && (l.debit != 0 || l.credit != 0))
                {
                    var docType = refType switch
                    {
                        "Sale" => PartyLedgerDocType.Sale,
                        "SaleReturn" => PartyLedgerDocType.SaleReturn,
                        "Purchase" => PartyLedgerDocType.Purchase,
                        "PurchaseReturn" => PartyLedgerDocType.PurchaseReturn,
                        "Receipt" => PartyLedgerDocType.Receipt,
                        "Payment" => PartyLedgerDocType.Payment,
                        _ => PartyLedgerDocType.Adjustment
                    };

                    _db.PartyLedgers.Add(new PartyLedger
                    {
                        PartyId = l.partyId.Value,
                        OutletId = outletId,
                        TimestampUtc = tsUtc,
                        DocType = docType,
                        DocId = refId ?? 0,
                        Description = memo,
                        Debit = l.debit,
                        Credit = l.credit
                    });
                }
            }

            if (Math.Round(dr, 2) != Math.Round(cr, 2))
                throw new InvalidOperationException($"Unbalanced journal (Dr {dr} != Cr {cr}).");

            await _db.SaveChangesAsync(ct);
            return j.Id;
        }

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

        public async Task PostSaleAsync(Sale sale)
        {
            var ts = sale.Ts;
            var outletId = sale.OutletId;

            // Your Sale model uses non-nullable decimals
            decimal paidNow = sale.CashAmount + sale.CardAmount;

            // Use the real sales tax field you have
            decimal tax = sale.TaxTotal;
            decimal revenuePortion = sale.Total - tax;

            decimal credit = sale.Total - paidNow;

            if (paidNow > 0m)
                await LineAsync(ts, outletId, CASH, paidNow, 0m, GlDocType.Sale, sale.Id, "Sale paid now");

            if (credit > 0m)
                await LineAsync(ts, outletId, AR, credit, 0m, GlDocType.Sale, sale.Id, "Sale on credit");

            // Revenue and output tax
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
            decimal revenuePortion = sale.Total - tax;

            decimal creditReversed = sale.Total - paidBack;

            if (paidBack > 0m)
                await LineAsync(ts, outletId, CASH, 0m, paidBack, GlDocType.SaleReturn, sale.Id, "Refund paid");

            if (creditReversed > 0m)
                await LineAsync(ts, outletId, AR, 0m, creditReversed, GlDocType.SaleReturn, sale.Id, "Reverse AR");

            // Reverse revenue and output tax
            if (revenuePortion != 0m)
                await LineAsync(ts, outletId, SALES, revenuePortion, 0m, GlDocType.SaleReturn, sale.Id, "Reverse revenue");

            if (tax > 0m)
                await LineAsync(ts, outletId, TAX, tax, 0m, GlDocType.SaleReturn, sale.Id, "Reverse output tax");

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


        public async Task PostPurchaseAsync(Purchase p)
        {
            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;

            // Inventory at net-of-tax (if your policy is to capitalize only the net)
            var goodsValue = p.GrandTotal - p.Tax;
            if (goodsValue != 0m)
                await LineAsync(ts, outletId, INV, goodsValue, 0m, GlDocType.Purchase, p.Id, "Inventory in");

            // Payment vs AP
            var paid = p.CashPaid;
            var due = p.GrandTotal - paid;

            if (paid > 0m)
                await LineAsync(ts, outletId, CASH, 0m, paid, GlDocType.Purchase, p.Id, "Paid now");

            if (due > 0m)
                await LineAsync(ts, outletId, AP, 0m, due, GlDocType.Purchase, p.Id, "AP created");

            // If you treat VAT as input-tax asset instead of part of inventory, add:
            // if (p.Tax > 0m) await LineAsync(ts, outletId, INPUT_TAX, p.Tax, 0m, GlDocType.Purchase, p.Id, "Input tax");

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

            if (paid > 0m)
                await LineAsync(ts, outletId, CASH, paid, 0m, GlDocType.PurchaseReturn, p.Id, "Refund received");

            if (due > 0m)
                await LineAsync(ts, outletId, AP, due, 0m, GlDocType.PurchaseReturn, p.Id, "Reduce AP");

            // If using input-tax separately:
            // if (p.Tax > 0m) await LineAsync(ts, outletId, INPUT_TAX, 0m, p.Tax, GlDocType.PurchaseReturn, p.Id, "Reverse input tax");

            await _db.SaveChangesAsync();
        }


        public async Task PostVoucherAsync(Voucher v)
        {
            foreach (var ln in v.Lines)
            {
                var acc = await _db.Accounts.FindAsync(ln.AccountId);
                if (acc == null) continue;

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

        public async Task PostPayrollAccrualAsync(PayrollRun run)
        {
            // Dr Salaries Expense (5130), Cr Salaries Payable (2200)
            var ts = run.CreatedAtUtc;
            var total = run.TotalNet; // you can post gross/deductions separately if needed

            if (total != 0m)
            {
                await LineAsync(ts, null, SALX, total, 0, GlDocType.PayrollAccrual, run.Id, "Payroll accrual");
                await LineAsync(ts, null, SALP, 0, total, GlDocType.PayrollAccrual, run.Id, "Salaries payable");
                await _db.SaveChangesAsync();
            }
        }

        public async Task PostPayrollPaymentAsync(PayrollRun run)
        {
            // Dr Salaries Payable (2200), Cr Cash (1000) or Bank (1010)
            var ts = DateTime.UtcNow;
            var total = run.TotalNet;
            if (total != 0m)
            {
                await LineAsync(ts, null, SALP, total, 0, GlDocType.PayrollPayment, run.Id, "Clear payable");
                await LineAsync(ts, null, CASH, 0, total, GlDocType.PayrollPayment, run.Id, "Payout");
                await _db.SaveChangesAsync();
            }
        }

        public async Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax)
        {
            // We post ONLY the delta vs. the original (your UI blocks negative deltas)
            var ts = newSale.UpdatedAtUtc ?? newSale.Ts;
            var outletId = newSale.OutletId;
            var deltaGrand = deltaSub + deltaTax;

            if (deltaGrand <= 0.005m)
                return; // nothing material to post

            // 1) Money side (assume difference was collected now → Cash; 
            // if you ever allow partial credit on amendments, split to AR similarly)
            await LineAsync(ts, outletId, CASH, deltaGrand, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: collect difference");

            // 2) Revenue & tax deltas
            if (deltaSub > 0) await LineAsync(ts, outletId, SALES, 0, deltaSub, GlDocType.SaleRevision, newSale.Id, "Amendment: revenue");
            if (deltaTax > 0) await LineAsync(ts, outletId, TAX, 0, deltaTax, GlDocType.SaleRevision, newSale.Id, "Amendment: tax");

            // 3) COGS & Inventory: compute from "SaleRev" stock entries for this revision
            var se = await _db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "SaleRev" && x.RefId == newSale.Id)
                .ToListAsync();

            // OUT lines (QtyChange < 0) increase COGS
            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0)
            {
                await LineAsync(ts, outletId, COGS, costOut, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: COGS");
                await LineAsync(ts, outletId, INV, 0, costOut, GlDocType.SaleRevision, newSale.Id, "Amendment: inventory out");
            }

            // IN lines (QtyChange > 0) reduce COGS (this can appear even if net revenue ↑)
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
            // deltaSub and deltaTax are signed differences between the NEW return revision
            // and the previous return revision.
            // Returns usually store negative totals; so:
            //   Example: latest.Subtotal = -100, amended.Subtotal = -120  => deltaSub = -20
            // Meaning: extra refund of 20 (+tax delta) compared to prior revision.

            var ts = amended.UpdatedAtUtc ?? amended.Ts;
            var outletId = amended.OutletId;
            var deltaGrand = deltaSub + deltaTax;

            // If essentially zero movement and no stock deltas, nothing to do.
            if (Math.Abs(deltaGrand) <= 0.005m)
            {
                // still check stock deltas below; if both sides zero, exit after stock part
            }

            // --- Money + Revenue/Tax (delta only) ---
            // For a RETURN, revenue was reversed in the base document.
            // If deltaGrand < 0  => more refund now:
            //   Dr Sales Revenue (abs deltaSub), Dr Output Tax (abs deltaTax), Cr Cash
            // If deltaGrand > 0  => customer returns some refund (collection):
            //   Dr Cash, Cr Sales Revenue (abs deltaSub), Cr Output Tax (abs deltaTax)
            if (deltaGrand < -0.005m)
            {
                var amt = Math.Abs(deltaGrand);
                if (Math.Abs(deltaSub) > 0.005m)
                    await LineAsync(ts, outletId, SALES, Math.Abs(deltaSub), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase revenue reversal");
                if (Math.Abs(deltaTax) > 0.005m)
                    await LineAsync(ts, outletId, TAX, Math.Abs(deltaTax), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase tax reversal");

                // Money out (refund extra). If you capture split in the UI, you can split to CASH/BANK here.
                await LineAsync(ts, outletId, CASH, 0, amt, GlDocType.SaleReturnRevision, amended.Id, "Return amend: extra refund");
            }
            else if (deltaGrand > 0.005m)
            {
                var amt = deltaGrand;

                // Money in (customer gives some back)
                await LineAsync(ts, outletId, CASH, amt, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: collect back");

                if (Math.Abs(deltaSub) > 0.005m)
                    await LineAsync(ts, outletId, SALES, 0, Math.Abs(deltaSub), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce revenue reversal");
                if (Math.Abs(deltaTax) > 0.005m)
                    await LineAsync(ts, outletId, TAX, 0, Math.Abs(deltaTax), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce tax reversal");
            }

            // --- COGS & Inventory from the stock deltas you wrote for this revision ---
            // Your EditReturn flow writes entries with RefType="Amend" and RefId=amended.Id
            var se = await _db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "Amend" && x.RefId == amended.Id)
                .ToListAsync();

            // QtyChange > 0 => inventory IN (return increased) -> Dr Inventory, Cr COGS
            var costIn = se.Where(x => x.QtyChange > 0).Sum(x => x.QtyChange * x.UnitCost);
            if (costIn > 0.005m)
            {
                await LineAsync(ts, outletId, INV, costIn, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory in");
                await LineAsync(ts, outletId, COGS, 0, costIn, GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce COGS");
            }

            // QtyChange < 0 => inventory OUT (return decreased) -> Dr COGS, Cr Inventory
            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0.005m)
            {
                await LineAsync(ts, outletId, COGS, costOut, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase COGS");
                await LineAsync(ts, outletId, INV, 0, costOut, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory out");
            }

            await _db.SaveChangesAsync();
        }



    }
}
