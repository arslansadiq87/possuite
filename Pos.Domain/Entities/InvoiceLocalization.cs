// Pos.Domain/Entities/InvoiceLocalization.cs
namespace Pos.Domain.Entities;

public class InvoiceLocalization
{
    public int Id { get; set; }
    public int InvoiceSettingsId { get; set; }

    // ISO language tag (e.g., "en", "ur", "ar", "fr-PK")
    public string Lang { get; set; } = "en";

    // Free-form rich text blocks used by receipt builder
    public string? Header { get; set; }          // printed at top (below outlet identity)
    public string? Footer { get; set; }          // printed at bottom (thanks/note)
    public string? SaleReturnNote { get; set; }  // printed on return receipts

    public InvoiceSettings InvoiceSettings { get; set; } = default!;
}