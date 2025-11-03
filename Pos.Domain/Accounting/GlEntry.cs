using Pos.Domain.Abstractions;
using Pos.Domain.Entities;   // <-- add this
using System;

namespace Pos.Domain.Accounting
{
    public enum GlDocType
    {
        Sale, SaleReturn, Purchase, PurchaseReturn,
        TillOpen, TillClose, CashReceipt, CashPayment,
        StockAdjust, JournalVoucher, PayrollAccrual, PayrollPayment,
        SaleRevision,
        SaleReturnRevision, VoucherRevision = 98, 
        VoucherVoid = 99
    }

    public class GlEntry : BaseEntity
    {
        public DateTime TsUtc { get; set; }
        public int? OutletId { get; set; }    // NULL => company scope

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;   // <-- now resolves

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        public GlDocType DocType { get; set; }
        public int DocId { get; set; }        // e.g., Sale.Id, Voucher.Id, PayrollRun.Id
        public string? Memo { get; set; }
    }
}
