using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Domain.Models.Hr;     // DTOs only

namespace Pos.Client.Wpf.Windows.Accounting
{
    // Row VM: explicit properties, no generator attributes
    public partial class PayrollItemVm : ObservableObject
    {
        public int Id { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; } = "";

        private decimal _basic;
        public decimal Basic
        {
            get => _basic;
            set { if (SetProperty(ref _basic, value)) OnPropertyChanged(nameof(Net)); }
        }

        private decimal _allowances;
        public decimal Allowances
        {
            get => _allowances;
            set { if (SetProperty(ref _allowances, value)) OnPropertyChanged(nameof(Net)); }
        }

        private decimal _overtime;
        public decimal Overtime
        {
            get => _overtime;
            set { if (SetProperty(ref _overtime, value)) OnPropertyChanged(nameof(Net)); }
        }

        private decimal _deductions;
        public decimal Deductions
        {
            get => _deductions;
            set { if (SetProperty(ref _deductions, value)) OnPropertyChanged(nameof(Net)); }
        }

        public decimal Net => Basic + Allowances + Overtime - Deductions;

        public static PayrollItemVm FromDto(PayrollItemDto d) => new()
        {
            Id = d.Id,
            StaffId = d.StaffId,
            StaffName = d.StaffName,
            Basic = d.Basic,
            Allowances = d.Allowances,
            Overtime = d.Overtime,
            Deductions = d.Deductions
        };

        public PayrollItemUpdateRequest ToUpdate() => new()
        {
            Id = Id,
            Basic = Basic,
            Allowances = Allowances,
            Overtime = Overtime,
            Deductions = Deductions
        };
    }

    // Screen VM: state only (NO commands here to avoid ambiguity)
    public partial class PayrollRunVm : ObservableObject
    {
        private DateTime _fromDate = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        public DateTime FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        private DateTime _toDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1).AddDays(-1);
        public DateTime ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

        private PayrollRunDto? _currentRun;
        public PayrollRunDto? CurrentRun
        {
            get => _currentRun;
            set
            {
                if (SetProperty(ref _currentRun, value))
                {
                    OnPropertyChanged(nameof(HasRun));
                    OnPropertyChanged(nameof(IsRunFinalized));
                }
            }
        }

        public bool HasRun => _currentRun is not null;
        public bool IsRunFinalized => _currentRun?.IsFinalized ?? false;

        private decimal _totalGross;
        public decimal TotalGross
        {
            get => _totalGross;
            set => SetProperty(ref _totalGross, value);
        }

        private decimal _totalDeductions;
        public decimal TotalDeductions
        {
            get => _totalDeductions;
            set => SetProperty(ref _totalDeductions, value);
        }

        private decimal _totalNet;
        public decimal TotalNet
        {
            get => _totalNet;
            set => SetProperty(ref _totalNet, value);
        }

        public ObservableCollection<PayrollItemVm> Items { get; } = new();
    }
}
