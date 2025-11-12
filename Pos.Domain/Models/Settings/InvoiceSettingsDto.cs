namespace Pos.Domain.Models.Settings
{
    public sealed record InvoiceSettingsDto(
        bool PrintOnSave,
        bool AskToPrintOnSave,
        int PaperWidthMm,
        int? SalesCardClearingAccountId,
        string? PrinterName,
        string FooterText  // resolved from localization
    );
}
