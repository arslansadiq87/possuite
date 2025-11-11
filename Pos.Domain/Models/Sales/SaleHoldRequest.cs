using System.Collections.Generic;

namespace Pos.Domain.Models.Sales
{
    public sealed class SaleHoldRequest
    {
        // Header
        public int OutletId { get; init; }
        public int CounterId { get; init; }
        public bool IsReturn { get; init; }
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
        public string? HoldTag { get; init; }
        public string? Footer { get; init; }

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
