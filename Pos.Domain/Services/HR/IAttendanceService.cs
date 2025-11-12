using System;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Hr;

namespace Pos.Domain.Services.Hr
{
    public interface IAttendanceService
    {
        Task PunchAsync(int staffId, bool isIn, string? source = null, CancellationToken ct = default);
        Task ComputeDayAsync(int staffId, DateTime dayUtc, CancellationToken ct = default);

        Task<IReadOnlyList<AttendancePunch>> GetPunchesForDayAsync(
            int staffId, DateTime dayUtc, CancellationToken ct = default);
    }
}
