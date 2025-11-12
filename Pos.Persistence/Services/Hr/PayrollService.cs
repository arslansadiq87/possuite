using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Domain.Models.Hr;
using Pos.Domain.Services;
using Pos.Domain.Services.Hr;
using Pos.Persistence;              // <-- ensure this is present for PosClientDbContext
using Pos.Persistence.Sync;
using System.Collections.Generic; // for IEnumerable<PayrollItem> in RecomputeTotals


namespace Pos.Persistence.Services.Hr
{
    /// <summary>
    /// EF Core-backed payroll service using IDbContextFactory and outbox for sync.
    /// </summary>
    public sealed class PayrollService : IPayrollService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;
        private readonly IOutboxWriter _outbox;

        public PayrollService(
            IDbContextFactory<PosClientDbContext> dbf,
            IGlPostingService gl,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _gl = gl;
            _outbox = outbox;
        }

        // ----------------- mapping helpers -----------------
        private static PayrollRunDto MapRun(PayrollRun r) => new()
        {
            Id = r.Id,
            PeriodStartUtc = r.PeriodStartUtc,
            PeriodEndUtc = r.PeriodEndUtc,
            IsFinalized = r.IsFinalized,
            PaidAtUtc = r.PaidAtUtc,
            TotalGross = r.TotalGross,
            TotalDeductions = r.TotalDeductions,
            TotalNet = r.TotalNet
        };

        private static PayrollRunSummaryDto MapSummary(PayrollRun r) => new()
        {
            TotalGross = r.TotalGross,
            TotalDeductions = r.TotalDeductions,
            TotalNet = r.TotalNet
        };

        private static PayrollItemDto MapItem(PayrollItem i, string staffName) => new()
        {
            Id = i.Id,
            StaffId = i.StaffId,
            StaffName = staffName,
            Basic = i.Basic,
            Allowances = i.Allowances,
            Overtime = i.Overtime,
            Deductions = i.Deductions
        };

        private static void RecomputeTotals(PayrollRun run, IEnumerable<PayrollItem> items)
        {
            var gross = 0m; var ded = 0m;
            foreach (var it in items)
            {
                gross += it.Basic + it.Allowances + it.Overtime;
                ded += it.Deductions;
            }
            run.TotalGross = gross;
            run.TotalDeductions = ded;
            run.TotalNet = gross - ded;
        }


        // ----------------- existing methods (CreateDraft/Finalize/Pay) remain as you had -----------------
        public async Task<PayrollRunDto> CreateDraftAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
        {
            if (endUtc <= startUtc) throw new ArgumentException("endUtc must be greater than startUtc.");

            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var run = new PayrollRun
            {
                PeriodStartUtc = startUtc,
                PeriodEndUtc = endUtc,
                CreatedAtUtc = DateTime.UtcNow,
                IsFinalized = false,
                PaidAtUtc = null
            };

            db.PayrollRuns.Add(run);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var staff = await db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new { s.Id, s.BasicSalary })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var s in staff)
            {
                db.PayrollItems.Add(new PayrollItem
                {
                    PayrollRunId = run.Id,
                    StaffId = s.Id,
                    Basic = s.BasicSalary,
                    Allowances = 0m,
                    Overtime = 0m,
                    Deductions = 0m
                });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var items = await db.PayrollItems
                .Where(i => i.PayrollRunId == run.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            RecomputeTotals(run, items);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _outbox.EnqueueUpsertAsync(db, run, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            // ✅ Return DTO, not entity
            return MapRun(run);
        }



        public async Task FinalizeAsync(int runId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Payroll run not found.");

            if (run.IsFinalized)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            run.IsFinalized = true;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _gl.PostPayrollAccrualAsync(run, ct).ConfigureAwait(false);

            await _outbox.EnqueueUpsertAsync(db, run, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }


        public async Task PayAsync(int runId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Payroll run not found.");

            if (!run.IsFinalized) throw new InvalidOperationException("Finalize payroll first.");
            if (run.PaidAtUtc is not null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            await _gl.PostPayrollPaymentAsync(run, ct).ConfigureAwait(false);

            run.PaidAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _outbox.EnqueueUpsertAsync(db, run, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        // ----------------- new query/update methods -----------------
        public async Task<PayrollRunDto?> GetRunAsync(int runId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            var r = await db.PayrollRuns.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == runId, ct)
                        .ConfigureAwait(false);
            return r is null ? null : MapRun(r);
        }

        public async Task<IReadOnlyList<PayrollItemDto>> GetItemsAsync(int runId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);

            var items = await db.PayrollItems
                .Where(i => i.PayrollRunId == runId)
                .Join(db.Staff.AsNoTracking(),
                      i => i.StaffId,
                      s => s.Id,
                      (i, s) => new { i, s.FullName })
                .AsNoTracking()
                .Select(x => new PayrollItemDto
                {
                    Id = x.i.Id,
                    StaffId = x.i.StaffId,
                    StaffName = x.FullName,
                    Basic = x.i.Basic,
                    Allowances = x.i.Allowances,
                    Overtime = x.i.Overtime,
                    Deductions = x.i.Deductions
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return items;
        }

        public async Task UpdateItemsAsync(int runId, IReadOnlyList<PayrollItemUpdateRequest> updates, CancellationToken ct = default)
        {
            if (updates.Count == 0) return;

            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("Payroll run not found.");
            if (run.IsFinalized) throw new InvalidOperationException("Run is finalized; edits are locked.");

            var ids = updates.Select(u => u.Id).ToArray();
            var rows = await db.PayrollItems.Where(p => p.PayrollRunId == runId && ids.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false);

            var byId = updates.ToDictionary(u => u.Id);
            foreach (var row in rows)
            {
                var u = byId[row.Id];
                row.Basic = u.Basic;
                row.Allowances = u.Allowances;
                row.Overtime = u.Overtime;
                row.Deductions = u.Deductions;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // recompute totals from DB (authoritative)
            var all = await db.PayrollItems.Where(i => i.PayrollRunId == runId).ToListAsync(ct).ConfigureAwait(false);
            RecomputeTotals(run, all);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _outbox.EnqueueUpsertAsync(db, run, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        public async Task<PayrollRunSummaryDto> GetSummaryAsync(int runId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            var run = await db.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Payroll run not found.");
            return MapSummary(run);
        }
    }
}
