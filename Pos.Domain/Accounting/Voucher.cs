using Pos.Domain.Abstractions;
using System;
using System.Collections.Generic;

namespace Pos.Domain.Accounting
{
    public enum VoucherType : short
    {
        // New canonical names (keep integer values stable!)
        Payment = 0,   // was Debit
        Receipt = 1,   // was Credit
        Journal = 2,

        // Back-compat aliases (optional; remove later)
        //[Obsolete("Use VoucherType.Payment")]
        //Debit = Payment,

        //[Obsolete("Use VoucherType.Receipt")]
        //Credit = Receipt
    }
    public enum VoucherStatus { Draft = 0, Posted = 1, Amended = 2, Voided = 3 }

    public class Voucher : BaseEntity
    {
        public DateTime TsUtc { get; set; } = DateTime.UtcNow;
        public int? OutletId { get; set; }
        public VoucherType Type { get; set; }
        public string? RefNo { get; set; }
        public string? Memo { get; set; }
        public List<VoucherLine> Lines { get; set; } = new();

        public VoucherStatus Status { get; set; } = VoucherStatus.Posted; // matches current behavior (save → posted)
        public int RevisionNo { get; set; } = 1;

        public int? AmendedFromId { get; set; }
        public DateTime? AmendedAtUtc { get; set; }

        public DateTime? VoidedAtUtc { get; set; }
        public string? VoidReason { get; set; }
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
