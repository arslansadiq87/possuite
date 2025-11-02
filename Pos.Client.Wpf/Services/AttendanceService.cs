using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IAttendanceService
    {
        Task PunchAsync(int staffId, bool isIn, string? source = null);
        Task ComputeDayAsync(int staffId, DateTime dayUtc);
    }

    public sealed class AttendanceService : IAttendanceService
    {
        private readonly PosClientDbContext _db;
        public AttendanceService(PosClientDbContext db) => _db = db;

        public async Task PunchAsync(int staffId, bool isIn, string? source = null)
        {
            _db.AttendancePunches.Add(new AttendancePunch
            {
                StaffId = staffId,
                IsIn = isIn,
                TsUtc = DateTime.UtcNow,
                Source = source
            });
            await _db.SaveChangesAsync();
        }

        public async Task ComputeDayAsync(int staffId, DateTime dayUtc)
        {
            var dayStart = dayUtc.Date;
            var dayEnd = dayStart.AddDays(1);
            var punches = await _db.AttendancePunches
                .Where(p => p.StaffId == staffId && p.TsUtc >= dayStart && p.TsUtc < dayEnd)
                .OrderBy(p => p.TsUtc).ToListAsync();

            TimeSpan worked = TimeSpan.Zero;
            for (int i = 0; i + 1 < punches.Count; i += 2)
                if (punches[i].IsIn && !punches[i + 1].IsIn)
                    worked += (punches[i + 1].TsUtc - punches[i].TsUtc);

            // Determine shift to compute LateBy (simple rule: pick active assignment at dayStart)
            var assign = await _db.ShiftAssignments
                .Include(a => a.Shift)
                .Where(a => a.StaffId == staffId && a.FromDateUtc <= dayStart && (a.ToDateUtc == null || a.ToDateUtc >= dayStart))
                .FirstOrDefaultAsync();

            TimeSpan lateBy = TimeSpan.Zero;
            if (assign != null && punches.Count > 0)
            {
                var scheduledIn = dayStart + assign.Shift.Start;
                var firstIn = punches.FirstOrDefault(p => p.IsIn)?.TsUtc ?? scheduledIn;
                if (firstIn > scheduledIn) lateBy = firstIn - scheduledIn;
            }

            var mark = punches.Count >= 2 ? AttendanceMark.Present : AttendanceMark.Absent;

            var day = await _db.AttendanceDays.FirstOrDefaultAsync(d => d.StaffId == staffId && d.DayUtc == dayStart);
            if (day == null)
            {
                day = new AttendanceDay { StaffId = staffId, DayUtc = dayStart };
                _db.AttendanceDays.Add(day);
            }
            day.Mark = mark;
            day.Worked = worked;
            day.LateBy = lateBy;

            await _db.SaveChangesAsync();
        }
    }
}
