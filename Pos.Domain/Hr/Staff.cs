using Pos.Domain.Abstractions;
using System;

namespace Pos.Domain.Hr
{
    public enum EmploymentType { FullTime, PartTime, Contract }
    public enum AttendanceMark { Present, Absent, Leave, Late, EarlyExit }

    public class Staff : BaseEntity
    {
        public string Code { get; set; } = "";        // Employee No
        public string FullName { get; set; } = "";
        public int? OutletId { get; set; }            // home outlet (optional)
        public EmploymentType Type { get; set; } = EmploymentType.FullTime;

        // Payroll
        public decimal BasicSalary { get; set; }      // monthly base
        public bool IsActive { get; set; } = true;
        public DateTime JoinedOnUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LeftOnUtc { get; set; }
    }

    public class Shift : BaseEntity
    {
        public string Name { get; set; } = "";        // e.g., Morning
        public TimeSpan Start { get; set; }           // 09:00
        public TimeSpan End { get; set; }             // 17:00
        public bool Overnight { get; set; }           // spans midnight?
    }

    public class ShiftAssignment : BaseEntity
    {
        public int StaffId { get; set; }
        public Staff Staff { get; set; } = null!;
        public int ShiftId { get; set; }
        public Shift Shift { get; set; } = null!;
        public DateTime FromDateUtc { get; set; }
        public DateTime? ToDateUtc { get; set; }      // null => open-ended
    }

    public class AttendancePunch : BaseEntity
    {
        public int StaffId { get; set; }
        public Staff Staff { get; set; } = null!;
        public DateTime TsUtc { get; set; }           // actual punch timestamp
        public bool IsIn { get; set; }                // true=in, false=out
        public string? Source { get; set; }           // manual/device/api
    }

    public class AttendanceDay : BaseEntity
    {
        public int StaffId { get; set; }
        public Staff Staff { get; set; } = null!;
        public DateTime DayUtc { get; set; }          // date bucket (00:00 UTC)
        public AttendanceMark Mark { get; set; }
        public TimeSpan Worked { get; set; }          // computed
        public TimeSpan LateBy { get; set; }          // computed
    }

    public class PayrollRun : BaseEntity
    {
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsFinalized { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalNet { get; set; }
    }

    public class PayrollItem : BaseEntity
    {
        public int PayrollRunId { get; set; }
        public PayrollRun PayrollRun { get; set; } = null!;
        public int StaffId { get; set; }
        public Staff Staff { get; set; } = null!;
        public decimal Basic { get; set; }
        public decimal Allowances { get; set; }
        public decimal Overtime { get; set; }
        public decimal Deductions { get; set; }
        public decimal Net => Basic + Allowances + Overtime - Deductions;
        public string? Notes { get; set; }
    }
}
