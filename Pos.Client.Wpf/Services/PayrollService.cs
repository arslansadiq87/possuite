using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IPayrollService
    {
        Task<PayrollRun> CreateDraftAsync(DateTime startUtc, DateTime endUtc);
        Task FinalizeAsync(int runId);
        Task PayAsync(int runId); // marks paid and posts GL payment
    }

    public sealed class PayrollService : IPayrollService
    {
        private readonly PosClientDbContext _db;
        private readonly IGlPostingService _gl;

        public PayrollService(PosClientDbContext db, IGlPostingService gl)
        {
            _db = db; _gl = gl;
        }

        public async Task<PayrollRun> CreateDraftAsync(DateTime startUtc, DateTime endUtc)
        {
            var run = new PayrollRun { PeriodStartUtc = startUtc, PeriodEndUtc = endUtc };
            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();

            var staff = await _db.Staff.Where(s => s.IsActive).ToListAsync();
            foreach (var s in staff)
            {
                // MVP: pro-rate not applied; you can refine with attendance later
                var item = new PayrollItem
                {
                    PayrollRunId = run.Id,
                    StaffId = s.Id,
                    Basic = s.BasicSalary,
                    Allowances = 0m,
                    Overtime = 0m,
                    Deductions = 0m
                };
                _db.PayrollItems.Add(item);
            }
            await _db.SaveChangesAsync();

            // totals
            var totals = await _db.PayrollItems.Where(i => i.PayrollRunId == run.Id)
                .Select(i => new { i.Basic, i.Allowances, i.Overtime, i.Deductions })
                .ToListAsync();

            run.TotalGross = totals.Sum(t => t.Basic + t.Allowances + t.Overtime);
            run.TotalDeductions = totals.Sum(t => t.Deductions);
            run.TotalNet = run.TotalGross - run.TotalDeductions;

            await _db.SaveChangesAsync();
            return run;
        }

        public async Task FinalizeAsync(int runId)
        {
            var run = await _db.PayrollRuns.FindAsync(runId);
            if (run == null) throw new InvalidOperationException("Payroll run not found.");
            if (run.IsFinalized) return;

            run.IsFinalized = true;
            await _db.SaveChangesAsync();

            // Post GL accrual
            await _gl.PostPayrollAccrualAsync(run);
        }

        public async Task PayAsync(int runId)
        {
            var run = await _db.PayrollRuns.FindAsync(runId);
            if (run == null) throw new InvalidOperationException("Payroll run not found.");
            if (!run.IsFinalized) throw new InvalidOperationException("Finalize payroll first.");

            await _gl.PostPayrollPaymentAsync(run);
        }
    }
}
