using System.Collections.Generic;

namespace Pos.Domain.Models.Sales
{
    public sealed class SaleFinalizeRequest
    {
        // Header
        public int OutletId { get; init; }
        public int CounterId { get; init; }
        public int TillSessionId { get; init; } // required open till
        public bool IsReturn { get; init; }
        public int? OriginalSaleId { get; init; }   // NEW: link when this is a return-from-invoice

        public int CashierId { get; init; }
        public int? SalesmanId { get; init; }
        public CustomerKind CustomerKind { get; init; }
        public int? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerPhone { get; init; }

        public decimal? InvoiceDiscountPct { get; init; }
        public decimal? InvoiceDiscountAmt { get; init; }
        public decimal InvoiceDiscountValue { get; init; }
        public decimal Subtotal { get; init; }
        public decimal TaxTotal { get; init; }
        public decimal Total { get; init; }

        // Payment
        public decimal CashAmount { get; init; }
        public decimal CardAmount { get; init; }
        public PaymentMethod PaymentMethod { get; init; }

        // UI bits
        public string? InvoiceFooter { get; init; }
        public string EReceiptToken { get; init; } = string.Empty;
        public string? Note { get; init; }          // NEW: reason / comment on the return

        // Held draft to void after success (optional)
        public int? HeldSaleId { get; init; }

        // Lines
        public List<SaleLineInput> Lines { get; init; } = new();

        public sealed record SaleLineInput(
            int ItemId,
            decimal Qty,
            decimal UnitPrice,
            decimal? DiscountPct,
            decimal? DiscountAmt,
            string? TaxCode,
            decimal TaxRatePct,
            bool TaxInclusive,
            decimal UnitNet,
            decimal LineNet,
            decimal LineTax,
            decimal LineTotal);
    }
}
