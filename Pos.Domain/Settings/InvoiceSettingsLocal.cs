// Pos.Domain.Settings/InvoiceSettingsLocal.cs
namespace Pos.Domain.Settings;

public enum DefaultBarcodeType
{
    Ean13 = 0,
    Ean8 = 1,
    Code128 = 2,
    Code39 = 3,
    Qr = 4
}

public class InvoiceSettingsLocal
{
    public int Id { get; set; }
    public int CounterId { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;

    // LOCAL (per-counter): printers only
    public string? PrinterName { get; set; }
    public string? LabelPrinterName { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
