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
    public partial class PayrollItemVm : ObservableObject
    {
        public int Id { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; } = "";
        [ObservableProperty] private decimal basic;
        [ObservableProperty] private decimal allowances;
        [ObservableProperty] private decimal overtime;
        [ObservableProperty] private decimal deductions;
        public decimal Net => Basic + Allowances + Overtime - Deductions;
    }

    public partial class PayrollRunVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IPayrollService _payroll;

        [ObservableProperty] private DateTime fromDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        [ObservableProperty] private DateTime toDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1).AddDays(-1);

        [ObservableProperty] private PayrollRun? currentRun;
        public ObservableCollection<PayrollItemVm> Items { get; } = new();

        public decimal TotalGross => Items.Sum(i => i.Basic + i.Allowances + i.Overtime);
        public decimal TotalDeductions => Items.Sum(i => i.Deductions);
        public decimal TotalNet => Items.Sum(i => i.Net);

        public PayrollRunVm(IDbContextFactory<PosClientDbContext> dbf, IPayrollService payroll)
        {
            _dbf = dbf; _payroll = payroll;
        }

        [RelayCommand]
        public async Task CreateDraftAsync()
        {
            var run = await _payroll.CreateDraftAsync(FromDate.Date, ToDate.Date.AddDays(1).AddTicks(-1));
            CurrentRun = run;
            await RefreshAsync();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (CurrentRun == null) return;
            Items.Clear();

            using var db = await _dbf.CreateDbContextAsync();
            var items = await db.PayrollItems
                .Include(p => p.Staff)
                .Where(p => p.PayrollRunId == CurrentRun.Id)
                .ToListAsync();

            foreach (var it in items)
            {
                Items.Add(new PayrollItemVm
                {
                    Id = it.Id,
                    StaffId = it.StaffId,
                    StaffName = it.Staff.FullName,
                    Basic = it.Basic,
                    Allowances = it.Allowances,
                    Overtime = it.Overtime,
                    Deductions = it.Deductions
                });
            }
            OnPropertyChanged(nameof(TotalGross));
            OnPropertyChanged(nameof(TotalDeductions));
            OnPropertyChanged(nameof(TotalNet));
        }

        [RelayCommand]
        public async Task FinalizeAsync()
        {
            if (CurrentRun == null) return;
            // push any edited numbers back first
            using (var db = await _dbf.CreateDbContextAsync())
            {
                foreach (var vm in Items)
                {
                    var row = await db.PayrollItems.FindAsync(vm.Id);
                    if (row == null) continue;
                    row.Basic = vm.Basic;
                    row.Allowances = vm.Allowances;
                    row.Overtime = vm.Overtime;
                    row.Deductions = vm.Deductions;
                }
                await db.SaveChangesAsync();
            }
            await _payroll.FinalizeAsync(CurrentRun.Id);
        }

        [RelayCommand]
        public async Task PayAsync()
        {
            if (CurrentRun == null) return;
            await _payroll.PayAsync(CurrentRun.Id);
        }
    }
}
