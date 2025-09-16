// Pos.Domain/Entities/Purchase.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    // Keep single, canonical stock ledger elsewhere (StockEntry). No duplicate StockTxn here.
    public enum PurchaseStatus { Draft = 0, Final = 1, Voided = 2 }
    public enum StockTargetType { Outlet = 1, Warehouse = 2 }
    public enum PurchasePaymentKind { Advance = 0, OnReceive = 1, Adjustment = 2 }
    public enum TenderMethod { Cash = 0, Card = 1, Bank = 2, Other = 3 }


    public class Purchase : BaseEntity
    {
        public int SupplierId { get; set; }
        public Supplier? Supplier { get; set; }

        // Where stock lands
        public StockTargetType TargetType { get; set; } = StockTargetType.Outlet;
        public int? OutletId { get; set; }
        public Outlet? Outlet { get; set; }
        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }

        // Docs & dates
        public string? VendorInvoiceNo { get; set; }
        public string? DocNo { get; set; }
        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow; // when created
        public DateTime? ReceivedAtUtc { get; set; }                  // when stock actually received

        // Money (snapshot)
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal OtherCharges { get; set; }
        public decimal GrandTotal { get; set; }

        public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

        // Optional denormalized settlement snapshot (service will keep in sync with Payments)
        public decimal CashPaid { get; set; }
        public decimal CreditDue { get; set; }

        public List<PurchaseLine> Lines { get; set; } = new();
        public List<PurchasePayment> Payments { get; set; } = new();
    }

    public class PurchaseLine : BaseEntity
    {
        public int PurchaseId { get; set; }
        public Purchase? Purchase { get; set; }

        public int ItemId { get; set; }
        public Item? Item { get; set; }

        public decimal Qty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Discount { get; set; }   // absolute per line
        public decimal TaxRate { get; set; }    // percent 0..100
        public decimal LineTotal { get; set; }  // computed snapshot

        public string? Notes { get; set; }
    }

    public class PurchasePayment : BaseEntity
    {
        public int PurchaseId { get; set; }
        public Purchase Purchase { get; set; } = null!;

        public int SupplierId { get; set; }
        public int OutletId { get; set; }

        public DateTime TsUtc { get; set; }

        public PurchasePaymentKind Kind { get; set; }
        public TenderMethod Method { get; set; }

        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    public class CashLedger : BaseEntity
    {
        public int OutletId { get; set; }
        public int? CounterId { get; set; }
        public int? TillSessionId { get; set; }

        public DateTime TsUtc { get; set; }
        public decimal Delta { get; set; }

        public string RefType { get; set; } = "";
        public int RefId { get; set; }
        public string? Note { get; set; }
    }
}
