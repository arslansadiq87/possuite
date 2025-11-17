// Pos.Domain/Models/Sales/EditSaleModels.cs
namespace Pos.Domain.Models.Sales
{
    public sealed record EditSaleLoadDto(
        int SaleId,
        int OutletId,
        int CounterId,
        int Revision,
        bool IsReturn,
        DateTime TsUtc,
        decimal Subtotal,
        decimal TaxTotal,
        decimal Total,
        decimal? InvoiceDiscountPct,
        decimal? InvoiceDiscountAmt,
        decimal InvoiceDiscountValue,
        CustomerKind CustomerKind,
        string? CustomerName,
        string? CustomerPhone,
        int? SalesmanId,
        string? InvoiceFooter,
        IReadOnlyList<EditSaleLoadDto.Line> Lines
    )
    {
        public sealed record Line(
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
            decimal LineTotal
        );
    }

    public sealed record EditSaleSaveRequest(
        int OriginalSaleId,
        int OutletId,
        int CounterId,
        int? TillSessionId,
        int NewRevisionNumber,                         // usually original.Revision + 1
        decimal Subtotal,
        decimal TaxTotal,
        decimal Total,
        decimal? InvoiceDiscountPct,
        decimal? InvoiceDiscountAmt,
        decimal InvoiceDiscountValue,
        int CashierId,
        int? SalesmanId,
        CustomerKind CustomerKind,
        string? CustomerName,
        string? CustomerPhone,
        decimal CollectedCash,
        decimal CollectedCard,
        PaymentMethod PaymentMethod,
        string? InvoiceFooter,
        IReadOnlyList<EditSaleSaveRequest.Line> Lines  // full replacement lines for new revision
    )
    {
        public sealed record Line(
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
            decimal LineTotal
        );
    }

    public sealed record EditSaleSaveResult(
        int NewSaleId,
        int NewRevision,
        decimal DeltaSubtotal,
        decimal DeltaTax,
        decimal DeltaGrand
    );
}
