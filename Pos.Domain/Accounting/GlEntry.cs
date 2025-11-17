using Pos.Domain.Abstractions;
using Pos.Domain.Entities;   // <-- add this
using System;

namespace Pos.Domain.Accounting
{
    public enum GlDocType
    {
        Sale, SaleReturn, Purchase, PurchaseRevision, PurchaseReturn,
        TillOpen, TillClose, CashReceipt, CashPayment,
        StockAdjust, JournalVoucher, PayrollAccrual, PayrollPayment,
        SaleRevision,
        SaleReturnRevision, VoucherRevision = 98, 
        VoucherVoid = 99
    }

    public enum GlDocSubType : short
    {
        Purchase_Gross = 10,
        Purchase_AmendDelta = 11,
        Purchase_Return = 12,
        Purchase_Payment = 13,
        Purchase_SupplierCredit = 14,

        Sale_Gross = 20,
        Sale_AmendDelta = 21,
        Sale_Return = 22,
        Sale_Receipt = 23,
        // NEW:
        Sale_COGS = 24,          // DR COGS / CR Inventory on sale
        Sale_Return_COGS = 25,   // DR Inventory / CR COGS on sale return


        Journal = 90,
        Other = 99
    }


    public class GlEntry : BaseEntity
    {
        public DateTime TsUtc { get; set; }
        public DateTime EffectiveDate { get; set; }  // posting date
        public int? OutletId { get; set; }    // NULL => company scope
        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;   // <-- now resolves
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }

        public GlDocType DocType { get; set; }
        public int DocId { get; set; }        // e.g., Sale.Id, Voucher.Id, PayrollRun.Id
        public string? DocNo { get; set; }

        public GlDocSubType DocSubType { get; set; }

        public Guid ChainId { get; set; }            // same for all revisions (e.g., Purchase.PublicId)
        public bool IsEffective { get; set; }        // only “current” rows true

        // Optional but useful
        public int? PartyId { get; set; }            // quick AP/AR joins by supplier/customer
        public string? Memo { get; set; }
        public int? LinkedPaymentId { get; set; }       // <-- NEW (nullable link to a specific payment)

    }
}
