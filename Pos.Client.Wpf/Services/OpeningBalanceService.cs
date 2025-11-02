using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Converts an OpeningBalance document to a Journal Voucher and posts it through GL.
    /// </summary>
    public sealed class OpeningBalanceService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;

        public OpeningBalanceService(IDbContextFactory<PosClientDbContext> dbf, IGlPostingService gl)
        {
            _dbf = dbf;
            _gl = gl;
        }

        public async Task PostOpeningAsync(int obId)
        {
            using var db = await _dbf.CreateDbContextAsync();
            var doc = await db.OpeningBalances
                              .Include(d => d.Lines)
                              .FirstAsync(d => d.Id == obId);

            if (doc.IsPosted)
                return;

            // ensure balancing equity account
            var equity = await db.Accounts.SingleOrDefaultAsync(a => a.Code == "E-OPEN");
            if (equity == null)
            {
                equity = new Account
                {
                    Code = "E-OPEN",
                    Name = "Equity: Opening Balance",
                    Type = AccountType.Equity,
                    NormalSide = NormalSide.Credit,
                    IsActive = true
                };
                db.Accounts.Add(equity);
                await db.SaveChangesAsync();
            }

            var totalDr = doc.Lines.Sum(x => x.Debit);
            var totalCr = doc.Lines.Sum(x => x.Credit);

            // Build a journal voucher
            var v = new Voucher
            {
                TsUtc = doc.AsOfDate,
                OutletId = doc.OutletId,
                RefNo = $"OB-{doc.Id}",
                Memo = doc.Memo ?? "Opening Balance",
                Type = VoucherType.Journal
            };
            db.Vouchers.Add(v);
            await db.SaveChangesAsync();

            foreach (var ln in doc.Lines)
            {
                db.VoucherLines.Add(new VoucherLine
                {
                    VoucherId = v.Id,
                    AccountId = ln.AccountId,
                    Description = "OB",
                    Debit = ln.Debit,
                    Credit = ln.Credit
                });
            }

            // Balance to equity if needed
            if (totalDr != totalCr)
            {
                var diff = totalDr - totalCr;
                db.VoucherLines.Add(new VoucherLine
                {
                    VoucherId = v.Id,
                    AccountId = equity.Id,
                    Description = "OB Balance",
                    Debit = diff < 0 ? -diff : 0m,
                    Credit = diff > 0 ? diff : 0m
                });
            }

            await db.SaveChangesAsync();

            // post via existing GL pipeline
            await _gl.PostVoucherAsync(v);

            doc.IsPosted = true;
            await db.SaveChangesAsync();
        }
    }
}
