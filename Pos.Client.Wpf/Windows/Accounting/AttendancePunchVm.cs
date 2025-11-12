using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Hr;
using Pos.Domain.Services;
using Pos.Domain.Services.Hr;
//using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AttendancePunchVm : ObservableObject
    {
        //private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IAttendanceService _svc;
        private readonly IStaffService _Staff;

        public ObservableCollection<Staff> Staff { get; } = new();
        public ObservableCollection<AttendancePunch> TodayPunches { get; } = new();

        [ObservableProperty] private Staff? selectedStaff;

        public AttendancePunchVm(IAttendanceService svc, IStaffService staff)
        {
            _svc = svc;
            _Staff = staff;
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            //using var db = await _dbf.CreateDbContextAsync();
            Staff.Clear();
            var list = await _Staff.GetAllActiveStaffAsync();
            //foreach (var s in await db.Staff.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName).ToListAsync())
            //    Staff.Add(s);
        }

        private async Task RefreshTodayAsync()
        {
            TodayPunches.Clear();
            if (SelectedStaff == null) return;

            var punches = await _svc.GetPunchesForDayAsync(SelectedStaff.Id, DateTime.UtcNow.Date);

            foreach (var p in punches) TodayPunches.Add(p);
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
