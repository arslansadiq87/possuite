// Pos.Domain/Entities/InvoiceLocalization.cs
namespace Pos.Domain.Entities;

public class InvoiceLocalization
{
    public int Id { get; set; }
    public int InvoiceSettingsId { get; set; }
    public string Lang { get; set; } = "en";
    public string? Header { get; set; }          // printed at top (below outlet identity)
    public string? Footer { get; set; }          // printed at bottom (thanks/note)
    public string? SaleReturnNote { get; set; }  // printed on return receipts
    public InvoiceSettings InvoiceSettings { get; set; } = default!;
}