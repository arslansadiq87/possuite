namespace Pos.Domain.Models.Sales
{
    // What the UI needs to render the grid and constraints
    public sealed record EditReturnLoadDto(
        int ReturnSaleId,
        int OriginalSaleId,
        int CounterId,
        int InvoiceNumber,
        int Revision,
        decimal CurrentTotal,
        IReadOnlyList<EditReturnLineDto> Lines
    );

    public sealed record EditReturnLineDto(
        int ItemId,
        string Sku,
        string Name,
        int SoldQty,                         // from original sale
        int AlreadyReturnedExcludingThis,    // across other returns
        int OldReturnQty,                    // abs(old) for this return’s current revision
        int AvailableQty,                    // clamp ceiling for new ReturnQty
        decimal UnitPrice,
        decimal? DiscountPct,
        decimal? DiscountAmt,
        decimal TaxRatePct,
        bool TaxInclusive
    );

    // UI → Service: finalize the amended return
    public sealed record EditReturnFinalizeRequest(
        int ReturnSaleId,
        string Reason,
        IReadOnlyList<EditReturnFinalizeLine> Lines,   // NEW ReturnQtys (absolute, clamped)
        decimal PayCash,                               // signed by direction (+collect / -refund)
        decimal PayCard                                // same sign rule
    );

    public sealed record EditReturnFinalizeLine(
        int ItemId,
        int ReturnQty,          // desired absolute qty for the amended doc
        decimal UnitPrice,
        decimal? DiscountPct,
        decimal? DiscountAmt,
        decimal TaxRatePct,
        bool TaxInclusive
    );

    public sealed record EditReturnFinalizeResult(
        int AmendedSaleId,
        int NewRevision,
        decimal DeltaSubtotal,  // signed delta vs previous revision
        decimal DeltaTax,       // signed delta vs previous revision
        decimal NewTotal        // signed
    );
}
