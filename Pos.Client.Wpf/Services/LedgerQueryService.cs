using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Persistence;
using System.Text.RegularExpressions;


namespace Pos.Client.Wpf.Services
{
    public record LedgerRow(DateTime TsUtc, string? Memo, decimal Debit, decimal Credit, decimal Running);

    // NEW: richer row for Cash Book UI (has Source/Till/Voided)
    public sealed class CashBookRowDto
    {
        public DateTime TsUtc { get; set; }
        public string? Memo { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Running { get; set; }
        public bool IsVoided { get; set; }
        public int? TillId { get; set; }
        public string? SourceRef { get; set; }  // "PO #x", "Sale #x", "Voucher PMT-xx", etc.
    }

    public interface ILedgerQueryService
    {
        Task<(decimal opening, List<LedgerRow> rows, decimal closing)>
            GetAccountLedgerAsync(int accountId, DateTime fromUtc, DateTime toUtc);

        Task<int> GetOutletCashAccountIdAsync(int outletId); // convenience for Cash Book

        // NEW: outlet-scoped cashbook (with includeVoided flag)
        Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided);
    }

    public sealed class LedgerQueryService : ILedgerQueryService
    {
        private readonly PosClientDbContext _db;
        private readonly ICoaService _coa;

        public LedgerQueryService(PosClientDbContext db, ICoaService coa)
        {
            _db = db;
            _coa = coa;
        }

        public async Task<int> GetOutletCashAccountIdAsync(int outletId)
            => await _coa.EnsureOutletCashAccountAsync(outletId);

        public async Task<(decimal opening, List<LedgerRow> rows, decimal closing)>
            GetAccountLedgerAsync(int accountId, DateTime fromUtc, DateTime toUtc)
        {
            // 1) Opening = account.OpeningDebit - OpeningCredit + GL before from
            var acct = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
            var openingBase = acct.OpeningDebit - acct.OpeningCredit;

            var prior = await _db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId && g.TsUtc < fromUtc)
                .Select(g => new { g.Debit, g.Credit })
                .ToListAsync();

            var priorSum = prior.Sum(x => x.Debit - x.Credit);
            var opening = openingBase + priorSum;

            // 2) In-range rows
            var inRange = await _db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId && g.TsUtc >= fromUtc && g.TsUtc <= toUtc)
                .OrderBy(g => g.TsUtc).ThenBy(g => g.Id)
                .Select(g => new { g.TsUtc, g.Memo, g.Debit, g.Credit })
                .ToListAsync();

            var rows = new List<LedgerRow>(inRange.Count);
            decimal running = opening;
            foreach (var r in inRange)
            {
                running += (r.Debit - r.Credit);
                rows.Add(new LedgerRow(r.TsUtc, r.Memo, r.Debit, r.Credit, running));
            }

            var closing = running;
            return (opening, rows, closing);
        }

        public async Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
        
        GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided)
        {
            // 🔒 Hard enforcement of outlet visibility
            if (!AuthZ.IsAdmin())
            {
                var myOutletId = AppState.Current.CurrentOutletId;
                if (outletId != myOutletId)
                    outletId = myOutletId; // or: throw new UnauthorizedAccessException();
            }

            // Resolve the outlet's Cash account using your existing helper
            var cashAccountId = await GetOutletCashAccountIdAsync(outletId);

            // Opening = account.OpeningDebit - OpeningCredit + GL before from
            var acct = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == cashAccountId);
            var openingBase = acct.OpeningDebit - acct.OpeningCredit;

            var prior = await _db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == cashAccountId && g.TsUtc < fromUtc)
                .Select(g => new { g.Debit, g.Credit })
                .ToListAsync();

            var priorSum = prior.Sum(x => x.Debit - x.Credit);
            var opening = openingBase + priorSum;

            // In-range rows for this cash account
            var q = _db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == cashAccountId && g.TsUtc >= fromUtc && g.TsUtc <= toUtc);

            // If you later add an IsVoided column on GlEntries, just uncomment:
            // if (!includeVoided) q = q.Where(g => !g.IsVoided);

            var inRange = await q
                .OrderBy(g => g.TsUtc).ThenBy(g => g.Id)
                .Select(g => new
                {
                    g.TsUtc,
                    g.Memo,
                    g.Debit,
                    g.Credit,
                    // Placeholders—when you share the real columns, we’ll map them:
                    // IsVoided = g.IsVoided,
                    // TillId   = g.TillId,
                    // PurchaseId = g.PurchaseId,
                    // SaleId     = g.SaleId,
                    // VoucherId  = g.VoucherId,
                    // VoucherType= g.VoucherType
                })
                .ToListAsync();

            var rows = new List<CashBookRowDto>(inRange.Count);
            decimal running = opening;

            foreach (var r in inRange)
            {
                running += (r.Debit - r.Credit);

                // --- Heuristic extraction from Memo (until FKs are wired) ---
                var memo = r.Memo ?? string.Empty;
                string? source = null;

                // Common patterns: "Voucher #2", "PO #1023", "Sale #S-22014"
                var m =
                    Regex.Match(memo, @"\bVoucher\s*#\s*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
                if (m.Success) source = $"Voucher #{m.Groups[1].Value}";
                if (source == null)
                {
                    m = Regex.Match(memo, @"\bPO\s*#\s*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (m.Success) source = $"PO #{m.Groups[1].Value}";
                }
                if (source == null)
                {
                    m = Regex.Match(memo, @"\bSale\s*#\s*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (m.Success) source = $"Sale #{m.Groups[1].Value}";
                }

                // Till hints
                if (source == null && memo.IndexOf("till close", StringComparison.OrdinalIgnoreCase) >= 0)
                    source = "Till Close";
                if (source == null && memo.IndexOf("till", StringComparison.OrdinalIgnoreCase) >= 0)
                    source = "Till";

                // Auto cash deltas still relate to vouchers; mark generically if nothing matched
                if (source == null && memo.IndexOf("voucher", StringComparison.OrdinalIgnoreCase) >= 0)
                    source = "Voucher";

                // Void detection (from your screenshot: "VOID of voucher #2")
                var isVoided = memo.StartsWith("VOID", StringComparison.OrdinalIgnoreCase)
                            || memo.IndexOf("VOID of voucher", StringComparison.OrdinalIgnoreCase) >= 0;

                // Optional: if user unchecked "Include voided", drop them here (since we don’t have IsVoided on DB yet)
                if (!includeVoided && isVoided)
                    continue;

                rows.Add(new CashBookRowDto
                {
                    TsUtc = r.TsUtc,
                    Memo = r.Memo,
                    Debit = r.Debit,
                    Credit = r.Credit,
                    Running = running,
                    IsVoided = isVoided,     // from memo until DB has a real flag
                    TillId = null,           // fill when GlEntries has TillId
                    SourceRef = source
                });
            }



            var closing = running;
            return (opening, rows, closing);
        }

    }
}
