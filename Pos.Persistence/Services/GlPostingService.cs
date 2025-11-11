// Pos.Persistence/Services/GlPostingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Hr;
using Pos.Domain.Services;          // IGlPostingService, ICoaService, IInvoiceSettingsService
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class GlPostingService : IGlPostingService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ICoaService _coa;
        private readonly IInvoiceSettingsService _invSettings;
        private readonly IOutboxWriter _outbox;

        public GlPostingService(
            IDbContextFactory<PosClientDbContext> dbf,
            ICoaService coa,
            IInvoiceSettingsService invSettings,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _coa = coa;
            _invSettings = invSettings;
            _outbox = outbox;
        }

        // CoA codes aligned with your template
        private const string BANK = "113";
        private const string INV = "1140";
        private const string AR = "6200";
        private const string AP = "6100";
        private const string TAX = "2110";
        private const string SALP = "2111";
        private const string SALES = "411";
        private const string COGS = "5111";
        private const string SALX = "52011";

        // -------------------- helpers --------------------
        private static async Task<int> IdOfAsync(PosClientDbContext db, string code, CancellationToken ct) =>
            (await db.Accounts.AsNoTracking().FirstAsync(a => a.Code == code, ct)).Id;

        private static void LineById(PosClientDbContext db, DateTime tsUtc, int? outletId, int accountId, decimal dr, decimal cr, GlDocType dt, int docId, string? memo = null)
        {
            db.GlEntries.Add(new GlEntry
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

        private static async Task Line(PosClientDbContext db, DateTime tsUtc, int? outletId, string code, decimal dr, decimal cr, GlDocType dt, int docId, string? memo, CancellationToken ct)
        {
            var id = await IdOfAsync(db, code, ct);
            LineById(db, tsUtc, outletId, id, dr, cr, dt, docId, memo);
        }

        // -------------------- Sales --------------------
        public async Task PostSaleAsync(Sale sale, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = sale.Ts;
            var outletId = sale.OutletId;
            var paidNow = sale.CashAmount + sale.CardAmount;
            var tax = sale.TaxTotal;
            var revenuePortion = sale.Total - tax;
            var credit = sale.Total - paidNow;

            if (sale.CashAmount > 0m)
            {
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId, ct);
                LineById(db, ts, outletId, tillAccId, sale.CashAmount, 0m, GlDocType.Sale, sale.Id, "Sale cash to Till");
            }
            if (sale.CardAmount > 0m)
            {
                var (s, _) = await _invSettings.GetAsync(outletId, "en", ct);
                if (s?.SalesCardClearingAccountId is null)
                    throw new InvalidOperationException("Cannot take CARD payments: configure a Sales Card Clearing Account in Invoice Settings for this outlet.");
                LineById(db, ts, outletId, s.SalesCardClearingAccountId.Value, sale.CardAmount, 0m, GlDocType.Sale, sale.Id, "Sale card receipt");
            }
            if (credit > 0m)
                await Line(db, ts, outletId, AR, credit, 0m, GlDocType.Sale, sale.Id, "Sale on credit", ct);

            if (revenuePortion != 0m)
                await Line(db, ts, outletId, SALES, 0m, revenuePortion, GlDocType.Sale, sale.Id, "Sales revenue", ct);
            if (tax > 0m)
                await Line(db, ts, outletId, TAX, 0m, tax, GlDocType.Sale, sale.Id, "Output tax", ct);

            var cost = await db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "Sale" && se.RefId == sale.Id && se.QtyChange < 0m)
                .SumAsync(se => (-se.QtyChange) * se.UnitCost, ct);

            if (cost != 0m)
            {
                await Line(db, ts, outletId, COGS, cost, 0m, GlDocType.Sale, sale.Id, "COGS", ct);
                await Line(db, ts, outletId, INV, 0m, cost, GlDocType.Sale, sale.Id, "Inventory out", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task PostSaleReturnAsync(Sale sale, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = sale.Ts;
            var outletId = sale.OutletId;
            var paidBack = sale.CashAmount + sale.CardAmount;
            var tax = sale.TaxTotal;
            var revenuePortion = sale.Total - tax;
            var creditReversed = sale.Total - paidBack;

            if (sale.CashAmount > 0m)
            {
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId, ct);
                LineById(db, ts, outletId, tillAccId, 0m, sale.CashAmount, GlDocType.SaleReturn, sale.Id, "Refund from Till");
            }
            if (sale.CardAmount > 0m)
            {
                var (s, _) = await _invSettings.GetAsync(outletId, "en", ct);
                if (s?.SalesCardClearingAccountId is null)
                    throw new InvalidOperationException("Cannot refund CARD: configure a Sales Card Clearing Account in Invoice Settings for this outlet.");
                LineById(db, ts, outletId, s.SalesCardClearingAccountId.Value, 0m, sale.CardAmount, GlDocType.SaleReturn, sale.Id, "Refund via bank/card");
            }

            if (creditReversed > 0m)
                await Line(db, ts, outletId, AR, 0m, creditReversed, GlDocType.SaleReturn, sale.Id, "Reverse AR", ct);

            if (revenuePortion != 0m)
                await Line(db, ts, outletId, SALES, revenuePortion, 0m, GlDocType.SaleReturn, sale.Id, "Reverse revenue", ct);
            if (tax > 0m)
                await Line(db, ts, outletId, TAX, tax, 0m, GlDocType.SaleReturn, sale.Id, "Reverse output tax", ct);

            var cost = await db.StockEntries.AsNoTracking()
                .Where(se => se.RefType == "SaleReturn" && se.RefId == sale.Id && se.QtyChange > 0m)
                .SumAsync(se => se.QtyChange * se.UnitCost, ct);

            if (cost != 0m)
            {
                await Line(db, ts, outletId, COGS, 0m, cost, GlDocType.SaleReturn, sale.Id, "Reverse COGS", ct);
                await Line(db, ts, outletId, INV, cost, 0m, GlDocType.SaleReturn, sale.Id, "Inventory in", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task PostSaleRevisionAsync(Sale newSale, decimal deltaSub, decimal deltaTax, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = newSale.UpdatedAtUtc ?? newSale.Ts;
            var outletId = newSale.OutletId;
            var deltaGrand = deltaSub + deltaTax;
            if (deltaGrand <= 0.005m) return;

            var tillAccId = await _coa.GetTillAccountIdAsync(outletId, ct);
            LineById(db, ts, outletId, tillAccId, deltaGrand, 0m, GlDocType.SaleRevision, newSale.Id, "Amendment: collect difference (Till)");

            if (deltaSub > 0) await Line(db, ts, outletId, SALES, 0m, deltaSub, GlDocType.SaleRevision, newSale.Id, "Amendment: revenue", ct);
            if (deltaTax > 0) await Line(db, ts, outletId, TAX, 0m, deltaTax, GlDocType.SaleRevision, newSale.Id, "Amendment: tax", ct);

            var se = await db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "SaleRev" && x.RefId == newSale.Id)
                .ToListAsync(ct);

            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0)
            {
                await Line(db, ts, outletId, COGS, costOut, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: COGS", ct);
                await Line(db, ts, outletId, INV, 0, costOut, GlDocType.SaleRevision, newSale.Id, "Amendment: inventory out", ct);
            }
            var costIn = se.Where(x => x.QtyChange > 0).Sum(x => x.QtyChange * x.UnitCost);
            if (costIn > 0)
            {
                await Line(db, ts, outletId, COGS, 0, costIn, GlDocType.SaleRevision, newSale.Id, "Amendment: reverse COGS", ct);
                await Line(db, ts, outletId, INV, costIn, 0, GlDocType.SaleRevision, newSale.Id, "Amendment: inventory in", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task PostReturnRevisionAsync(Sale amended, decimal deltaSub, decimal deltaTax, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = amended.UpdatedAtUtc ?? amended.Ts;
            var outletId = amended.OutletId;
            var deltaGrand = deltaSub + deltaTax;

            if (deltaGrand < -0.005m)
            {
                if (Math.Abs(deltaSub) > 0.005m)
                    await Line(db, ts, outletId, SALES, Math.Abs(deltaSub), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase revenue reversal", ct);
                if (Math.Abs(deltaTax) > 0.005m)
                    await Line(db, ts, outletId, TAX, Math.Abs(deltaTax), 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase tax reversal", ct);

                var tillAccId = await _coa.GetTillAccountIdAsync(outletId, ct);
                LineById(db, ts, outletId, tillAccId, 0, Math.Abs(deltaGrand), GlDocType.SaleReturnRevision, amended.Id, "Return amend: extra refund (Till)");
            }
            else if (deltaGrand > 0.005m)
            {
                var tillAccId = await _coa.GetTillAccountIdAsync(outletId, ct);
                LineById(db, ts, outletId, tillAccId, deltaGrand, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: collect back (Till)");

                if (Math.Abs(deltaSub) > 0.005m)
                    await Line(db, ts, outletId, SALES, 0, Math.Abs(deltaSub), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce revenue reversal", ct);
                if (Math.Abs(deltaTax) > 0.005m)
                    await Line(db, ts, outletId, TAX, 0, Math.Abs(deltaTax), GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce tax reversal", ct);
            }

            var se = await db.StockEntries.AsNoTracking()
                .Where(x => x.RefType == "Amend" && x.RefId == amended.Id)
                .ToListAsync(ct);

            var costIn = se.Where(x => x.QtyChange > 0).Sum(x => x.QtyChange * x.UnitCost);
            if (costIn > 0.005m)
            {
                await Line(db, ts, outletId, INV, costIn, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory in", ct);
                await Line(db, ts, outletId, COGS, 0, costIn, GlDocType.SaleReturnRevision, amended.Id, "Return amend: reduce COGS", ct);
            }
            var costOut = se.Where(x => x.QtyChange < 0).Sum(x => (-x.QtyChange) * x.UnitCost);
            if (costOut > 0.005m)
            {
                await Line(db, ts, outletId, COGS, costOut, 0, GlDocType.SaleReturnRevision, amended.Id, "Return amend: increase COGS", ct);
                await Line(db, ts, outletId, INV, 0, costOut, GlDocType.SaleReturnRevision, amended.Id, "Return amend: inventory out", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        // -------------------- Purchases --------------------
        public async Task PostPurchaseAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;
            var gross = p.GrandTotal;

            if (gross != 0m)
            {
                await Line(db, ts, outletId, INV, gross, 0m, GlDocType.Purchase, p.Id, $"PO #{p.DocNo} · Inventory in (gross)", ct);
                await Line(db, ts, outletId, AP, 0m, gross, GlDocType.Purchase, p.Id, $"PO #{p.DocNo} · To AP (gross)", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        public async Task PostPurchaseReturnAsync(Purchase p, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = p.ReceivedAtUtc ?? p.PurchaseDate;
            var outletId = p.OutletId;
            var gross = p.GrandTotal;

            if (gross != 0m)
            {
                await Line(db, ts, outletId, INV, 0m, gross, GlDocType.PurchaseReturn, p.Id, $"PR #{p.DocNo} · Inventory out (gross)", ct);
                await Line(db, ts, outletId, AP, gross, 0m, GlDocType.PurchaseReturn, p.Id, $"PR #{p.DocNo} · Reduce AP (gross)", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        // -------------------- Vouchers --------------------
        public async Task PostVoucherAsync(Voucher v, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            await db.Entry(v).Collection(x => x.Lines).LoadAsync(ct);

            var outletId = v.OutletId;
            var ts = v.TsUtc;

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

            if (v.Type == VoucherType.Debit)
            {
                if (totalDr <= 0m) throw new InvalidOperationException("Debit Voucher must have Debit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(v.OutletId, ct);
                LineById(db, ts, outletId, cashId, 0m, totalDr, GlDocType.CashPayment, v.Id, "Cash payment (auto)");
            }
            else if (v.Type == VoucherType.Credit)
            {
                if (totalCr <= 0m) throw new InvalidOperationException("Credit Voucher must have Credit > 0.");
                var cashId = await _coa.GetCashAccountIdAsync(v.OutletId, ct);
                LineById(db, ts, outletId, cashId, totalCr, 0m, GlDocType.CashReceipt, v.Id, "Cash receipt (auto)");
            }
            else
            {
                if (Math.Abs(totalDr - totalCr) > 0.004m)
                    throw new InvalidOperationException("Journal Voucher must balance.");
            }

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, v, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task PostVoucherVoidAsync(Voucher voucherToVoid, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var baseDocTypes = new[] { GlDocType.JournalVoucher, GlDocType.CashPayment, GlDocType.CashReceipt };
            var lines = await db.GlEntries
                .Where(g => g.DocId == voucherToVoid.Id && baseDocTypes.Contains(g.DocType))
                .AsNoTracking()
                .ToListAsync(ct);

            if (lines.Count == 0) return;

            var ts = DateTime.UtcNow;
            foreach (var g in lines)
            {
                db.GlEntries.Add(new GlEntry
                {
                    TsUtc = ts,
                    OutletId = g.OutletId,
                    AccountId = g.AccountId,
                    Debit = g.Credit,
                    Credit = g.Debit,
                    DocType = GlDocType.VoucherVoid,
                    DocId = voucherToVoid.Id,
                    Memo = $"VOID of voucher #{voucherToVoid.Id}"
                });
            }

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, voucherToVoid, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task PostVoucherRevisionAsync(Voucher newVoucher, IReadOnlyList<VoucherLine> oldLines, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            await db.Entry(newVoucher).Collection(x => x.Lines).LoadAsync(ct);

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

            if (newVoucher.Type == VoucherType.Debit || newVoucher.Type == VoucherType.Credit)
            {
                var cashId = await _coa.GetCashAccountIdAsync(newVoucher.OutletId, ct);

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

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, newVoucher, ct);
            await db.SaveChangesAsync(ct);
        }

        // -------------------- Payroll --------------------
        public async Task PostPayrollAccrualAsync(PayrollRun run, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = run.CreatedAtUtc;
            var total = run.TotalNet;
            if (total == 0m) return;

            await Line(db, ts, null, SALX, total, 0, GlDocType.PayrollAccrual, run.Id, "Payroll accrual", ct);
            await Line(db, ts, null, SALP, 0, total, GlDocType.PayrollAccrual, run.Id, "Salaries payable", ct);

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, run, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task PostPayrollPaymentAsync(PayrollRun run, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = DateTime.UtcNow;
            var total = run.TotalNet;
            if (total == 0m) return;

            await Line(db, ts, null, SALP, total, 0, GlDocType.PayrollPayment, run.Id, "Clear payable", ct);

            var handAccId = await _coa.GetCashAccountIdAsync(outletId: null, ct);
            LineById(db, ts, null, handAccId, 0, total, GlDocType.PayrollPayment, run.Id, "Payout (Cash)");

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, run, ct);
            await db.SaveChangesAsync(ct);
        }

        // -------------------- Till close --------------------
        public async Task PostTillCloseAsync(TillSession session, decimal declaredCash, decimal systemCash, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var ts = session.CloseTs ?? DateTime.UtcNow;
            var outletId = session.OutletId;

            var tillId = await _coa.GetTillAccountIdAsync(outletId, ct);
            var handId = await _coa.GetCashAccountIdAsync(outletId, ct);

            var move = declaredCash;
            var overShort = declaredCash - systemCash;

            if (move != 0m)
            {
                LineById(db, ts, outletId, handId, move, 0m, GlDocType.TillClose, session.Id, "Till close: move declared cash to Hand");
                LineById(db, ts, outletId, tillId, 0m, move, GlDocType.TillClose, session.Id, "Till close");
            }

            if (Math.Abs(overShort) > 0.005m)
            {
                if (overShort > 0)
                {
                    var otherIncomeId = await IdOfAsync(db, "49", ct);   // adjust to your chart if needed
                    LineById(db, ts, outletId, otherIncomeId, 0m, overShort, GlDocType.TillClose, session.Id, "Cash over");
                }
                else
                {
                    var cashShortId = await IdOfAsync(db, "541", ct);
                    LineById(db, ts, outletId, cashShortId, -overShort, 0m, GlDocType.TillClose, session.Id, "Cash short");
                }
            }

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, session, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
