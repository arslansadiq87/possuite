// Pos.Domain/Entities/Sale.cs
using System;
using Pos.Domain;               // enums you already use
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Sale : BaseEntity
    {
        public DateTime Ts { get; set; }
        public int OutletId { get; set; }
        public int CounterId { get; set; }
        public int? TillSessionId { get; set; }

        // Returns (legacy flags you already had)
        public bool IsReturn { get; set; }
        public int? OriginalSaleId { get; set; }

        // -------- NEW: Self-reference for returns/amendments (ADD THESE) --------
        public int? RefSaleId { get; set; }     // points to original sale
        public Sale? RefSale { get; set; }

        // -------- NEW: Revision model --------
        public int Revision { get; set; } = 0;
        public int? RevisedFromSaleId { get; set; }
        public int? RevisedToSaleId { get; set; }

        // -------- Status / audit --------
        public SaleStatus Status { get; set; } = SaleStatus.Draft;
        public string? HoldTag { get; set; }
        public string? CustomerName { get; set; }
        public string? Note { get; set; }

        // Soft-delete audit (void)
        public string? VoidReason { get; set; }
        public DateTime? VoidedAtUtc { get; set; }

        public int InvoiceNumber { get; set; } // unique per Counter (with Revision)

        // Invoice discounts
        public decimal? InvoiceDiscountPct { get; set; }
        public decimal? InvoiceDiscountAmt { get; set; }
        public decimal InvoiceDiscountValue { get; set; }
        public bool DiscountBeforeTax { get; set; } = true;

        // Totals
        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal Total { get; set; }

        // Users
        public int CashierId { get; set; }
        public int? SalesmanId { get; set; }

        // Customer
        public CustomerKind CustomerKind { get; set; } = CustomerKind.WalkIn;
        public int? CustomerId { get; set; }
        public string? CustomerPhone { get; set; }

        // Payment
        public decimal CashAmount { get; set; }
        public decimal CardAmount { get; set; }
        public PaymentMethod PaymentMethod { get; set; }

        // e-receipt
        public string? EReceiptToken { get; set; }
        public string? EReceiptUrl { get; set; }

        // Footer
        public string? InvoiceFooter { get; set; }
    }
}
