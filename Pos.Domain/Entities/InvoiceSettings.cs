using Pos.Domain.Entities;

public class InvoiceSettings
{
    public int Id { get; set; }
    public int? OutletId { get; set; }

    // Identity
    public string? OutletDisplayName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Phone { get; set; }

    // NEW: NTN/FBR (Pakistan)
    public string? BusinessNtn { get; set; }             // printable NTN
    public bool ShowBusinessNtn { get; set; } = false;

    public bool EnableFbr { get; set; } = false;         // enable FBR mode
    public bool ShowFbrQr { get; set; } = false;         // print FBR QR at bottom
    public string? FbrPosId { get; set; }                // your POS ID per FBR
    public string? FbrApiBaseUrl { get; set; }           // future use
    public string? FbrAuthKey { get; set; }              // future use (secure storage later)

    // Printer & paper
    public string? PrinterName { get; set; }
    public int PaperWidthMm { get; set; } = 80;
    public bool EnableDrawerKick { get; set; } = true;

    // ROW VISIBILITY – items section
    public bool RowShowProductName { get; set; } = true;
    public bool RowShowProductSku { get; set; } = false;
    public bool RowShowQty { get; set; } = true;
    public bool RowShowUnitPrice { get; set; } = true;
    public bool RowShowLineDiscount { get; set; } = false;
    public bool RowShowLineTotal { get; set; } = true;

    // HEADER/IDENTITY
    public bool ShowBusinessName { get; set; } = true;
    public bool ShowAddress { get; set; } = true;
    public bool ShowContacts { get; set; } = true;
    public bool ShowLogo { get; set; } = false;   // you already have LogoPng; this lets user hide it quickly

    // TOTALS BLOCK VISIBILITY
    public bool TotalsShowTaxes { get; set; } = true;
    public bool TotalsShowDiscounts { get; set; } = true;
    public bool TotalsShowOtherExpenses { get; set; } = true;
    public bool TotalsShowGrandTotal { get; set; } = true;
    public bool TotalsShowPaymentRecv { get; set; } = true;
    public bool TotalsShowBalance { get; set; } = true;

    // FOOTER
    public bool ShowFooter { get; set; } = true;
    // New: print behavior
    public bool PrintOnSave { get; set; }           // auto-print after saving sale
    public bool AskToPrintOnSave { get; set; }      // show Yes/No prompt on save

    // New: receipt logo
    public byte[]? LogoPng { get; set; }            // raw PNG bytes
    public int LogoMaxWidthPx { get; set; } = 384;  // clamp for 80mm, adjust to ESC/POS width
    public string? LogoAlignment { get; set; } = "Center"; // Left|Center|Right

    // Receipt toggles
    public bool ShowQr { get; set; } = false;
    public bool ShowCustomerOnReceipt { get; set; } = true;
    public bool ShowCashierOnReceipt { get; set; } = true;
    public bool PrintBarcodeOnReceipt { get; set; } = false; // NEW: e.g. invoice barcode/QR

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Outlet? Outlet { get; set; }
    public ICollection<InvoiceLocalization> Localizations { get; set; } = new List<InvoiceLocalization>();
    // --- NEW: per-outlet posting defaults ---
    public int? PurchaseBankAccountId { get; set; }          // default bank/clearing for supplier payments (Bank/Card)
    public int? SalesCardClearingAccountId { get; set; }     // default bank/clearing for sales card receipts
                                                             // --- END NEW ---
    public bool UseTill { get; set; } = true; // NEW: per-outlet toggle. If false => no open till required, cash posts to Cash-in-Hand

}
