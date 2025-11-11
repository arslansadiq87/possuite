namespace Pos.Domain.Models.Sales
{
    public sealed record ItemIndexDto(
        int Id,
        string Name,
        string Sku,
        string Barcode,
        decimal Price,
        string? TaxCode,
        decimal DefaultTaxRatePct,
        bool TaxInclusive,
        decimal? DefaultDiscountPct,
        decimal? DefaultDiscountAmt,
        string? ProductName,
        string? Variant1Name,
        string? Variant1Value,
        string? Variant2Name,
        string? Variant2Value
    )
    {
        public string DisplayName => Formatting.ProductNameComposer.Compose(
            ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);
    }
}
