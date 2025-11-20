// Pos.Domain.Settings/InvoiceSettingsScoped.cs
namespace Pos.Domain.Settings;

public class InvoiceSettingsScoped
{
    public int Id { get; set; }

    /// <summary>
    /// null = Global scope. If set => Outlet scope.
    /// </summary>
    public int? OutletId { get; set; }

    // Print behavior / Drawer (scoped)
    public bool CashDrawerKickEnabled { get; set; }
    public bool AutoPrintOnSave { get; set; }
    public bool AskBeforePrint { get; set; } = true;

    // Display timezone for UI timestamps (scoped)
    public string? DisplayTimeZoneId { get; set; }

    // Accounts (store AccountId)
    public int? SalesCardClearingAccountId { get; set; }
    public int? PurchaseBankAccountId { get; set; }

    // Items defaults
    public DefaultBarcodeType DefaultBarcodeType { get; set; } = DefaultBarcodeType.Ean13;

    // Footers (scoped)
    public string? FooterSale { get; set; }
    public string? FooterSaleReturn { get; set; }
    public string? FooterVoucher { get; set; }
    public string? FooterZReport { get; set; }

    // Backups (scoped)
    public bool EnableDailyBackup { get; set; }
    public bool EnableHourlyBackup { get; set; }

    // Till requirement (scoped)
    public bool UseTill { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
