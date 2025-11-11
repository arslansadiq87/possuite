// Pos.Persistence/Services/LedgerQueryService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Models.Accounting;
using Pos.Domain.Services;                 // ILedgerQueryService, ICoaService
using Pos.Persistence;

namespace Pos.Persistence.Services
{
    public sealed class LedgerQueryService : ILedgerQueryService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ICoaService _coa;

        public LedgerQueryService(IDbContextFactory<PosClientDbContext> dbf, ICoaService coa)
        {
            _dbf = dbf;
            _coa = coa;
        }

        public async Task<int> GetOutletCashAccountIdAsync(int outletId, CancellationToken ct = default)
            => await _coa.EnsureOutletCashAccountAsync(outletId, ct);

        public async Task<(decimal opening, List<LedgerRow> rows, decimal closing)>
            GetAccountLedgerAsync(int accountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var acct = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);
            var openingBase = acct.OpeningDebit - acct.OpeningCredit;

            var prior = await db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId && g.TsUtc < fromUtc)
                .Select(g => new { g.Debit, g.Credit })
                .ToListAsync(ct);

            var opening = openingBase + prior.Sum(x => x.Debit - x.Credit);

            var inRange = await db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId && g.TsUtc >= fromUtc && g.TsUtc <= toUtc)
                .OrderBy(g => g.TsUtc).ThenBy(g => g.Id)
                .Select(g => new { g.TsUtc, g.Memo, g.Debit, g.Credit })
                .ToListAsync(ct);

            var rows = new List<LedgerRow>(inRange.Count);
            decimal running = opening;

            foreach (var r in inRange)
            {
                running += (r.Debit - r.Credit);
                rows.Add(new LedgerRow(r.TsUtc, r.Memo, r.Debit, r.Credit, running));
            }

            return (opening, rows, running);
        }

        public Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CancellationToken ct = default)
            => GetCashBookAsync(outletId, fromUtc, toUtc, includeVoided, CashBookScope.HandOnly, ct);

        public async Task<(decimal opening, List<CashBookRowDto> rows, decimal closing)>
            GetCashBookAsync(int outletId, DateTime fromUtc, DateTime toUtc, bool includeVoided, CashBookScope scope, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Resolve per-outlet accounts via CoA helpers
            var cashAccountId = await _coa.EnsureOutletCashAccountAsync(outletId, ct); // 11101-OUT
            var tillAccountId = await _coa.EnsureOutletTillAccountAsync(outletId, ct); // 11102-OUT

            var accountIds = scope switch
            {
                CashBookScope.HandOnly => new[] { cashAccountId },
                CashBookScope.TillOnly => new[] { tillAccountId },
                CashBookScope.Both => new[] { cashAccountId, tillAccountId },
                _ => new[] { cashAccountId }
            };

            // Opening across selected accounts
            async Task<decimal> OpeningForAsync(int accountId)
            {
                var acct = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);
                var openingBase = acct.OpeningDebit - acct.OpeningCredit;

                var prior = await db.GlEntries.AsNoTracking()
                    .Where(g => g.AccountId == accountId && g.TsUtc < fromUtc)
                    .Select(g => new { g.Debit, g.Credit })
                    .ToListAsync(ct);

                return openingBase + prior.Sum(x => x.Debit - x.Credit);
            }

            decimal opening = 0m;
            foreach (var id in accountIds)
                opening += await OpeningForAsync(id);

            // In-range GL for all selected accounts merged & ordered
            var gls = await db.GlEntries.AsNoTracking()
                .Where(g => accountIds.Contains(g.AccountId) && g.TsUtc >= fromUtc && g.TsUtc <= toUtc)
                .OrderBy(g => g.TsUtc).ThenBy(g => g.Id)
                .Select(g => new
                {
                    g.AccountId,
                    g.TsUtc,
                    g.Memo,
                    g.Debit,
                    g.Credit
                })
                .ToListAsync(ct);

            // Memo parsers
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
                    if (tag.Equals("Voucher", StringComparison.OrdinalIgnoreCase)) return $"Voucher {no}";
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

            static bool IsMemoVoided(string? memoRaw)
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
                if (!includeVoided && isVoided) continue;

                running += (e.Debit - e.Credit);

                rows.Add(new CashBookRowDto
                {
                    TsUtc = e.TsUtc,
                    Memo = e.Memo,
                    Debit = e.Debit,
                    Credit = e.Credit,
                    Running = running,
                    IsVoided = isVoided,
                    TillId = (scope == CashBookScope.TillOnly || (scope == CashBookScope.Both && e.AccountId == tillAccountId))
                                ? tillAccountId : (int?)null,
                    SourceRef = BuildSourceFromMemo(e.Memo)
                });
            }

            return (opening, rows, running);
        }
    }
}
