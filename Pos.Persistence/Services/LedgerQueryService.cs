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

            // Opening balance uses EFFECTIVE rows strictly before range start
            var prior = await db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId
                         && g.IsEffective
                         && g.EffectiveDate < fromUtc)
                .Select(g => new { g.Debit, g.Credit })
                .ToListAsync(ct);

            var opening = openingBase + prior.Sum(x => x.Debit - x.Credit);

            // In-range uses EFFECTIVE rows and EffectiveDate within [from, to)
            var inRange = await db.GlEntries.AsNoTracking()
                .Where(g => g.AccountId == accountId
                         && g.IsEffective
                         && g.EffectiveDate >= fromUtc
                         && g.EffectiveDate < toUtc)
                .OrderBy(g => g.EffectiveDate).ThenBy(g => g.Id)
                .Select(g => new { g.EffectiveDate, g.Memo, g.DocNo, g.Debit, g.Credit })
                .ToListAsync(ct);

            var rows = new List<LedgerRow>(inRange.Count);
            decimal running = opening;

            foreach (var r in inRange)
            {
                running += (r.Debit - r.Credit);
                rows.Add(new LedgerRow(r.EffectiveDate, r.Memo + (r.DocNo), r.Debit, r.Credit, running));
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

            // Resolve per-outlet accounts via CoA (OK to "ensure" here since this is read-only flow outside transactions)
            var cashAccountId = await _coa.EnsureOutletCashAccountAsync(outletId, ct);
            var accountIds = scope switch
            {
                CashBookScope.HandOnly => new[] { cashAccountId },
                CashBookScope.TillOnly => new[] { await _coa.EnsureOutletTillAccountAsync(outletId, ct) },
                CashBookScope.Both => new[] { cashAccountId, await _coa.EnsureOutletTillAccountAsync(outletId, ct) },
                _ => new[] { cashAccountId }
            };

            // Opening across selected accounts (EffectiveDate & IsEffective)
            async Task<decimal> OpeningForAsync(int accountId)
            {
                var acct = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);
                var openingBase = acct.OpeningDebit - acct.OpeningCredit;

                var prior = await db.GlEntries.AsNoTracking()
                    .Where(g => g.AccountId == accountId
                             && g.EffectiveDate < fromUtc
                             && g.IsEffective)
                    .Select(g => new { g.Debit, g.Credit })
                    .ToListAsync(ct);

                return openingBase + prior.Sum(x => x.Debit - x.Credit);
            }

            decimal opening = 0m;
            foreach (var id in accountIds)
                opening += await OpeningForAsync(id);

            // In-range GL for all selected accounts merged & ordered (EffectiveDate, IsEffective)
            var gls = await db.GlEntries.AsNoTracking()
                .Where(g => accountIds.Contains(g.AccountId)
                         && g.EffectiveDate >= fromUtc
                         && g.EffectiveDate <= toUtc
                         && (includeVoided || g.IsEffective))
                .OrderBy(g => g.EffectiveDate).ThenBy(g => g.Id)
                .Select(g => new {
                    g.AccountId,
                    g.EffectiveDate,
                    g.DocType,
                    g.DocSubType,
                    g.DocNo,
                    g.Memo,
                    g.Debit,
                    g.Credit,
                    g.IsEffective
                })
                .ToListAsync(ct);

            string BuildSource(string? memoRaw, string docType, string subType, string? docNo)
            {
                if (!string.IsNullOrWhiteSpace(docNo))
                    return $"{docType} {subType} #{docNo}";
                if (!string.IsNullOrWhiteSpace(memoRaw))
                    return memoRaw!;
                return $"{docType} {subType}";
            }

            var rows = new List<CashBookRowDto>(gls.Count);
            decimal running = opening;

            foreach (var e in gls)
            {
                if (!includeVoided && !e.IsEffective) continue;

                running += (e.Debit - e.Credit);

                rows.Add(new CashBookRowDto
                {
                    TsUtc = e.EffectiveDate, // show accounting date
                    Memo = e.Memo,
                    Debit = e.Debit,
                    Credit = e.Credit,
                    Running = running,
                    IsVoided = !e.IsEffective,
                    TillId = (scope == CashBookScope.TillOnly || (scope == CashBookScope.Both && e.AccountId != cashAccountId))
                                ? e.AccountId : (int?)null,
                    SourceRef = BuildSource(e.Memo, e.DocType.ToString(), e.DocSubType.ToString(), e.DocNo)
                });
            }

            return (opening, rows, running);
        }

    }
}
