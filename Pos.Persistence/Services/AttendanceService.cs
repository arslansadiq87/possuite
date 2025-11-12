using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Domain.Services.Hr;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// Attendance service (client-side persistence). Short-lived DbContext per call.
    /// </summary>
    public sealed class AttendanceService : IAttendanceService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public AttendanceService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task PunchAsync(int staffId, bool isIn, string? source = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Basic guard: ignore obviously bad staff ids
            if (staffId <= 0) return;

            var punch = new AttendancePunch
            {
                StaffId = staffId,
                IsIn = isIn,
                TsUtc = DateTime.UtcNow,
                Source = source
            };

            await db.AttendancePunches.AddAsync(punch, ct);
            await db.SaveChangesAsync(ct);

            // SYNC: use the tracked entity (no "last row" query race)
            await _outbox.EnqueueUpsertAsync(db, punch, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task ComputeDayAsync(int staffId, DateTime dayUtc, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var dayStart = dayUtc.Date;           // already UTC (callers pass UTC)
            var dayEnd = dayStart.AddDays(1);

            var punches = await db.AttendancePunches
                .Where(p => p.StaffId == staffId && p.TsUtc >= dayStart && p.TsUtc < dayEnd)
                .OrderBy(p => p.TsUtc)
                .ToListAsync(ct);

            // Pair IN -> OUT in order; skip odd tails safely
            var worked = TimeSpan.Zero;
            for (int i = 0; i + 1 < punches.Count; i += 2)
            {
                var a = punches[i];
                var b = punches[i + 1];
                if (a.IsIn && !b.IsIn && b.TsUtc > a.TsUtc)
                    worked += (b.TsUtc - a.TsUtc);
            }

            // Determine shift active at dayStart to compute LateBy
            var assign = await db.ShiftAssignments
                .Include(a => a.Shift)
                .Where(a => a.StaffId == staffId
                            && a.FromDateUtc <= dayStart
                            && (a.ToDateUtc == null || a.ToDateUtc >= dayStart))
                .FirstOrDefaultAsync(ct);

            var lateBy = TimeSpan.Zero;
            if (assign != null && punches.Count > 0)
            {
                var scheduledIn = dayStart + assign.Shift.Start;
                var firstIn = punches.FirstOrDefault(p => p.IsIn)?.TsUtc ?? scheduledIn;
                if (firstIn > scheduledIn)
                    lateBy = firstIn - scheduledIn;
            }

            var mark = punches.Count >= 2 ? AttendanceMark.Present : AttendanceMark.Absent;

            var day = await db.AttendanceDays
                .FirstOrDefaultAsync(d => d.StaffId == staffId && d.DayUtc == dayStart, ct);

            if (day is null)
            {
                day = new AttendanceDay { StaffId = staffId, DayUtc = dayStart };
                await db.AttendanceDays.AddAsync(day, ct);
            }

            day.Mark = mark;
            day.Worked = worked;
            day.LateBy = lateBy;

            await db.SaveChangesAsync(ct);

            // SYNC: daily summary
            await _outbox.EnqueueUpsertAsync(db, day, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<AttendancePunch>> GetPunchesForDayAsync(
            int staffId, DateTime dayUtc, CancellationToken ct = default)
        {
            var d0 = dayUtc.Date;           // already UTC
            var d1 = d0.AddDays(1);

            await using var db = await _dbf.CreateDbContextAsync(ct);
            var list = await db.AttendancePunches.AsNoTracking()
                .Where(p => p.StaffId == staffId && p.TsUtc >= d0 && p.TsUtc < d1)
                .OrderBy(p => p.TsUtc)
                .ToListAsync(ct);

            return list;
        }
    }
}
