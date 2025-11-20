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
    // Invoice printing
    public string? PrinterName { get; set; }
    public bool CashDrawerKickEnabled { get; set; }
    public bool AutoPrintOnSave { get; set; }
    public bool AskBeforePrint { get; set; }
    // NEW: Label printing (barcode labels etc.)
    public string? LabelPrinterName { get; set; }
    // NEW: Display timezone for UI timestamps
    public string? DisplayTimeZoneId { get; set; }   // e.g., "Asia/Karachi" or Windows TZ id
    // NEW: Till payment accounts
    public int? SalesCardClearingAccountId { get; set; }  // POS card settlement suspense/clearing
    public int? PurchaseBankAccountId { get; set; }       // default bank for purchase payments
    // NEW: Items defaults
    public DefaultBarcodeType DefaultBarcodeType { get; set; } = DefaultBarcodeType.Ean13;
    // Footers
    public string? FooterSale { get; set; }
    public string? FooterSaleReturn { get; set; }
    public string? FooterVoucher { get; set; }
    public string? FooterZReport { get; set; }
    // add to class InvoiceSettingsLocal
    public bool EnableDailyBackup { get; set; }      // local flag
    public bool EnableHourlyBackup { get; set; }     // local flag
    public bool UseTill { get; set; } = true; // NEW: per-outlet toggle. If false => no open till required, cash posts to Cash-in-Hand

    public DateTime UpdatedAtUtc { get; set; }
}
