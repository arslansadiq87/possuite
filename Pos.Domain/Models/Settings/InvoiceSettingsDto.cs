namespace Pos.Domain.Models.Settings
{
    public sealed record InvoiceSettingsDto(
    bool PrintOnSave,
    bool AskToPrintOnSave
 
);

}
