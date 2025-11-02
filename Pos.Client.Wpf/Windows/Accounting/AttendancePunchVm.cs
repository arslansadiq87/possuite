using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AttendancePunchVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IAttendanceService _svc;

        public ObservableCollection<Staff> Staff { get; } = new();
        public ObservableCollection<AttendancePunch> TodayPunches { get; } = new();

        [ObservableProperty] private Staff? selectedStaff;

        public AttendancePunchVm(IDbContextFactory<PosClientDbContext> dbf, IAttendanceService svc)
        {
            _dbf = dbf; _svc = svc;
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();
            Staff.Clear();
            foreach (var s in await db.Staff.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName).ToListAsync())
                Staff.Add(s);
        }

        private async Task RefreshTodayAsync()
        {
            TodayPunches.Clear();
            if (SelectedStaff == null) return;

            var d0 = DateTime.UtcNow.Date;
            var d1 = d0.AddDays(1);

            using var db = await _dbf.CreateDbContextAsync();
            var list = await db.AttendancePunches.AsNoTracking()
                .Where(p => p.StaffId == SelectedStaff.Id && p.TsUtc >= d0 && p.TsUtc < d1)
                .OrderBy(p => p.TsUtc).ToListAsync();
            foreach (var p in list) TodayPunches.Add(p);
        }

        partial void OnSelectedStaffChanged(Staff? value) => _ = RefreshTodayAsync();

        [RelayCommand]
        public async Task PunchInAsync()
        {
            if (SelectedStaff == null) return;
            await _svc.PunchAsync(SelectedStaff.Id, true, "manual");
            await RefreshTodayAsync();
        }

        [RelayCommand]
        public async Task PunchOutAsync()
        {
            if (SelectedStaff == null) return;
            await _svc.PunchAsync(SelectedStaff.Id, false, "manual");
            await RefreshTodayAsync();
        }

        [RelayCommand]
        public async Task ComputeTodayAsync()
        {
            if (SelectedStaff == null) return;
            await _svc.ComputeDayAsync(SelectedStaff.Id, DateTime.UtcNow.Date);
        }
    }
}
