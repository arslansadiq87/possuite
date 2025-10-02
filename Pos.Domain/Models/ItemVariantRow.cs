// Pos.Client.Wpf/Models/ItemVariantRow.cs
namespace Pos.Domain.Models
{
    public sealed class ItemVariantRow
    {
        public int Id { get; init; }
        public string Sku { get; init; } = "";
        public string Name { get; init; } = "";           // Item.Name (shown only in Standalone mode)
        public string? ProductName { get; init; }         // Product.Name if any

        public string? Barcode { get; init; }
        public decimal Price { get; init; }

        public string? Variant1Name { get; init; }
        public string? Variant1Value { get; init; }
        public string? Variant2Name { get; init; }
        public string? Variant2Value { get; init; }

        public int? BrandId { get; init; }
        public string? BrandName { get; init; }           // Item.Brand ?? Product.Brand

        public int? CategoryId { get; init; }
        public string? CategoryName { get; init; }        // Item.Category ?? Product.Category

        public string? TaxCode { get; init; }
        public decimal DefaultTaxRatePct { get; init; }
        public bool TaxInclusive { get; init; }

        public decimal? DefaultDiscountPct { get; init; }
        public decimal? DefaultDiscountAmt { get; init; }

        public System.DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsVoided { get; set; }
        public DateTime? VoidedAtUtc { get; set; }
        public string? VoidedBy { get; set; }


        public string DisplayName =>
            Pos.Domain.Formatting.ProductNameComposer.Compose(
                ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);
    }
}
