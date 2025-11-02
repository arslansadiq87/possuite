using Pos.Domain.Abstractions;
using System;
using System.Collections.Generic;

namespace Pos.Domain.Accounting
{
    public enum VoucherType { Debit, Credit, Journal }

    public class Voucher : BaseEntity
    {
        public DateTime TsUtc { get; set; } = DateTime.UtcNow;
        public int? OutletId { get; set; }
        public VoucherType Type { get; set; }
        public string? RefNo { get; set; }
        public string? Memo { get; set; }
        public List<VoucherLine> Lines { get; set; } = new();
    }

    public class VoucherLine : BaseEntity
    {
        public int VoucherId { get; set; }
        public Voucher Voucher { get; set; } = null!;
        public int AccountId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? Description { get; set; }
    }
}
