namespace Pos.Domain.Models.Sales
{
    public sealed record InvoiceSearchRowDto(
        int SaleId,
        int CounterId,
        int InvoiceNumber,
        int Revision,
        Pos.Domain.SaleStatus Status,
        bool IsReturn,
        DateTime TsUtc,
        string Customer,
        decimal Total
    );

    public sealed record InvoiceDetailHeaderDto(
        int SaleId,
        int CounterId,
        int InvoiceNumber,
        int Revision,
        Pos.Domain.SaleStatus Status,
        bool IsReturn,
        DateTime TsUtc,
        decimal Total
    );

    public sealed record InvoiceDetailLineDto(
        int ItemId,
        string Sku,
        string DisplayName,
        int Qty,
        decimal UnitPrice,
        decimal LineTotal
    );

    public sealed record HeldRowDto(
        int Id,
        DateTime TsUtc,
        string? HoldTag,
        string? CustomerName,
        decimal Total
    );

}
