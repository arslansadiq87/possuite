using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Infrastructure; // AuthZ, AppState
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public record LedgerRow(DateTime TsUtc, string? Memo, decimal Debit, decimal Credit, decimal Running);

    // Richer row for Cash Book UI (has Source/Till/Voided-ish via memo)
    public sealed class CashBookRowDto
    {
        public DateTime TsUtc { get; set; }
        public string? Memo { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Running { get; set; }
        public bool IsVoided { get; set; }    // inferred from memo text until DB column exists
        public int? TillId { get; set; }      // stays null until GlEntry has this column
        public string? SourceRef { get; set; } // "PO #x", "Sale #x", "Voucher PMT-xx", etc.
    }

    public enum CashBookScope
    {
        HandOnly = 0,   // 11101-OUT  (current behavior)
        TillOnly = 1,   // 11102-OUT
        Both = 2    // 11101-OUT + 11102-OUT
    }
    public interface ILedgerQueryService
    {

        Task<(decimal opening, List<LedgerRow> rows, decimal closing)>
            GetAccountLedgerAsync(int accountId, DateTime fromUtc, DateTime toUtc);

        Task<int> GetOutletCashAccountIdAsync(int outletId); // convenience for Cash Book

        Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided);

        // NEW: scoped variant
        Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CashBookScope scope);
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
            // Opening = account.OpeningDebit - OpeningCredit + GL before from
            var acct = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
            var openingBase = acct.OpeningDebit - acct.OpeningCredit;

            var prior = await _db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId && g.TsUtc < fromUtc)
                .Select(g => new { g.Debit, g.Credit })
                .ToListAsync();

            var opening = openingBase + prior.Sum(x => x.Debit - x.Credit);

            // In-range rows
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

            return (opening, rows, running);
        }

        public async Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
    GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided)
        {
            // preserve old behavior by default (Cash-in-Hand only)
            return await GetCashBookAsync(outletId, fromUtc, toUtc, includeVoided, CashBookScope.HandOnly);
        }

        // NEW
        public async Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CashBookScope scope)
        {
            // Enforce outlet visibility (unchanged)
            if (!AuthZ.IsAdmin())
            {
                var myOutletId = AppState.Current.CurrentOutletId;
                if (outletId != myOutletId)
                    outletId = myOutletId;
            }

            // Resolve per-outlet accounts
            var cashAccountId = await _coa.EnsureOutletCashAccountAsync(outletId); // 11101-OUT
            var tillAccountId = await _coa.EnsureOutletTillAccountAsync(outletId); // 11102-OUT

            var accountIds = scope switch
            {
                CashBookScope.HandOnly => new[] { cashAccountId },
                CashBookScope.TillOnly => new[] { tillAccountId },
                CashBookScope.Both => new[] { cashAccountId, tillAccountId },
                _ => new[] { cashAccountId }
            };

            // --- opening balance across selected accounts ---
            async Task<decimal> OpeningForAsync(int accountId)
            {
                var acct = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
                var openingBase = acct.OpeningDebit - acct.OpeningCredit;

                var prior = await _db.GlEntries.AsNoTracking()
                    .Where(g => g.AccountId == accountId && g.TsUtc < fromUtc)
                    .Select(g => new { g.Debit, g.Credit })
                    .ToListAsync();

                return openingBase + prior.Sum(x => x.Debit - x.Credit);
            }

            decimal opening = 0m;
            foreach (var id in accountIds)
                opening += await OpeningForAsync(id);

            // --- in-range GL for all selected accounts merged & ordered ---
            var gls = await _db.GlEntries.AsNoTracking()
                .Where(g => accountIds.Contains(g.AccountId) && g.TsUtc >= fromUtc && g.TsUtc <= toUtc)
                .OrderBy(g => g.TsUtc).ThenBy(g => g.Id)
                .Select(g => new
                {
                    g.AccountId,
                    g.TsUtc,
                    g.Memo,
                    g.Debit,
                    g.Credit
                    // GlEntry currently lacks IsVoided/TillId columns in your code
                })
                .ToListAsync();

            // (keep your existing regex/helpers exactly as you wrote them)
            var rxVoucherTag = new Regex(@"\b(Voucher|PMT|RCV|JRN|GEN)[\s\-:]*#?\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxVoucherPlain = new Regex(@"\bVoucher\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxPo = new Regex(@"\b(PO|Purchase\s*Invoice|Purchase)\s*#?\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxSale = new Regex(@"\b(Sale|Invoice|SI)\s*#?\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxSaleRet = new Regex(@"\b(Sale\s*Return|SR)\s*#?\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxPurRet = new Regex(@"\b(Purchase\s*Return|PR)\s*#?\s*([A-Za-z0-9\-_/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var rxTillClose = new Regex(@"\b(Till\s*Close|Z\s*Close|Z\-Close)\b(?:\s*#?\s*([A-Za-z0-9\-_/]+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            string? BuildSourceFromMemo(string? memoRaw)
            {
                var memo = memoRaw ?? string.Empty;
                var m = rxVoucherTag.Match(memo);
                if (m.Success)
                {
                    var tag = m.Groups[1].Value.Trim();
                    var no = m.Groups[2].Value.Trim();
                    if (tag.Equals("Voucher", StringComparison.OrdinalIgnoreCase))
                        return $"Voucher {no}";
                    return $"Voucher {tag.ToUpperInvariant()}-{no}";
                }
                if (rxVoucherPlain.IsMatch(memo)) return "Voucher";
                m = rxPo.Match(memo);
                if (m.Success) return (m.Groups[1].Value.StartsWith("PO", StringComparison.OrdinalIgnoreCase) ? "PO" : "Purchase") + $" #{m.Groups[2].Value}";
                m = rxSale.Match(memo);
                if (m.Success) return "Sale #" + m.Groups[2].Value;
                m = rxSaleRet.Match(memo);
                if (m.Success) return "Sale Return #" + m.Groups[2].Value;
                m = rxPurRet.Match(memo);
                if (m.Success) return "Purchase Return #" + m.Groups[2].Value;
                m = rxTillClose.Match(memo);
                if (m.Success)
                {
                    var no = m.Groups.Count >= 3 ? m.Groups[2].Value : "";
                    return string.IsNullOrWhiteSpace(no) ? "Till Close" : $"Till Close {no}";
                }
                if (memo.IndexOf("till close", StringComparison.OrdinalIgnoreCase) >= 0) return "Till Close";
                if (memo.IndexOf("till", StringComparison.OrdinalIgnoreCase) >= 0) return "Till";
                if (memo.IndexOf("supplier payment", StringComparison.OrdinalIgnoreCase) >= 0) return "Supplier Payment";
                if (memo.IndexOf("customer receipt", StringComparison.OrdinalIgnoreCase) >= 0) return "Customer Receipt";
                if (memo.IndexOf("sale cash", StringComparison.OrdinalIgnoreCase) >= 0) return "Sale (Cash)";
                if (memo.IndexOf("card", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    memo.IndexOf("sale", StringComparison.OrdinalIgnoreCase) >= 0) return "Sale (Card)";
                return string.IsNullOrWhiteSpace(memo) ? "—" : memo;
            }

            bool IsMemoVoided(string? memoRaw)
            {
                if (string.IsNullOrWhiteSpace(memoRaw)) return false;
                var memo = memoRaw.Trim();
                return memo.StartsWith("VOID", StringComparison.OrdinalIgnoreCase)
                    || memo.IndexOf("voided", StringComparison.OrdinalIgnoreCase) >= 0
                    || memo.IndexOf("VOID of voucher", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            var rows = new List<CashBookRowDto>(gls.Count);
            decimal running = opening;

            foreach (var e in gls)
            {
                var isVoided = IsMemoVoided(e.Memo);
                if (!includeVoided && isVoided)
                    continue;

                running += (e.Debit - e.Credit);

                rows.Add(new CashBookRowDto
                {
                    TsUtc = e.TsUtc,
                    Memo = e.Memo,
                    Debit = e.Debit,
                    Credit = e.Credit,
                    Running = running,
                    IsVoided = isVoided,
                    // simple tag: if TillOnly single-stream we can mark; for Both we could mark by AccountId:
                    TillId = (scope == CashBookScope.TillOnly || (scope == CashBookScope.Both && e.AccountId == tillAccountId))
                                ? tillAccountId : null,
                    SourceRef = BuildSourceFromMemo(e.Memo)
                });
            }

            return (opening, rows, running);
        }

    }
}
