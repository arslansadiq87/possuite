namespace Pos.Domain.DTO;

public sealed record ItemIndexDto(
    int Id,
    string Name,
    string Sku,
    string Barcode,                // primary or any fallback
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
    public string DisplayName =>
        Pos.Domain.Formatting.ProductNameComposer.Compose(
            ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);

    // NEW: stock at source location
    public decimal OnHand { get; init; }

    // NEW: brand/category display
    public string? Brand { get; init; }
    public string? Category { get; init; }
}
