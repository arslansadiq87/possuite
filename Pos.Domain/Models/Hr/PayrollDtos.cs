using System;
using System.Collections.Generic;

namespace Pos.Domain.Models.Hr
{
    public sealed class PayrollRunDto
    {
        public int Id { get; set; }
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public bool IsFinalized { get; set; }
        public DateTime? PaidAtUtc { get; set; }

        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalNet { get; set; }
    }

    public sealed class PayrollItemDto
    {
        public int Id { get; set; }
        public int StaffId { get; set; }
        public string StaffName { get; set; } = "";
        public decimal Basic { get; set; }
        public decimal Allowances { get; set; }
        public decimal Overtime { get; set; }
        public decimal Deductions { get; set; }
        public decimal Net => Basic + Allowances + Overtime - Deductions;
    }

    public sealed class PayrollItemUpdateRequest
    {
        public int Id { get; set; }
        public decimal Basic { get; set; }
        public decimal Allowances { get; set; }
        public decimal Overtime { get; set; }
        public decimal Deductions { get; set; }
    }

    public sealed class PayrollRunSummaryDto
    {
        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalNet { get; set; }
    }
}
