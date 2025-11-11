using System.Collections.Generic;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.Sales
{
    public sealed class SaleResumeDto
    {
        // Header snapshot
        public int SaleId { get; init; }
        public bool IsReturn { get; init; }
        public decimal? InvoiceDiscountPct { get; init; }
        public decimal? InvoiceDiscountAmt { get; init; }
        public string? InvoiceFooter { get; init; }
        public CustomerKind CustomerKind { get; init; }
        public int? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerPhone { get; init; }
        public int? SalesmanId { get; init; }

        // Lines
        public List<SaleLineRow> Lines { get; init; } = new();

        public sealed record SaleLineRow(
            int ItemId,
            string Sku,
            string DisplayName,
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
