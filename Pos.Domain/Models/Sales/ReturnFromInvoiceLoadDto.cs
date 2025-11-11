namespace Pos.Domain.Models.Sales
{
    // Top-level line DTO (no nesting)
    public sealed record ReturnFromInvoiceLineDto(
        int ItemId,
        string Sku,
        string Name,
        int SoldQty,
        int AlreadyReturned,
        int AvailableQty,
        decimal UnitPrice,
        decimal? DiscountPct,
        decimal? DiscountAmt,
        decimal TaxRatePct,
        bool TaxInclusive
    );

    public sealed record ReturnFromInvoiceLoadDto(
        int SaleId,
        int OutletId,
        int CounterId,
        int Revision,
        string HeaderHuman,
        IReadOnlyList<ReturnFromInvoiceLineDto> Lines
    );
}
