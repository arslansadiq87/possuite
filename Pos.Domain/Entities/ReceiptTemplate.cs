using System;
using System.Collections.Generic;

namespace Pos.Domain.Entities
{
    public class ReceiptTemplate
    {
        public int Id { get; set; }
        public int? OutletId { get; set; }      // null => Global/default
        public ReceiptDocType DocType { get; set; }

        // Printer & page
        public string? PrinterName { get; set; }
        public int PaperWidthMm { get; set; } = 80;
        public bool EnableDrawerKick { get; set; } = true;

        // Identity (moved from InvoiceSettings)
        public string? OutletDisplayName { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Phone { get; set; }

        // Logo
        public bool ShowLogoOnReceipt { get; set; } = true;
        public byte[]? LogoPng { get; set; }
        public int LogoMaxWidthPx { get; set; } = 384;
        public string LogoAlignment { get; set; } = "Center";

        // Row & totals flags (same semantics as now)
        public bool RowShowProductName { get; set; } = true;
        public bool RowShowProductSku { get; set; } = false;
        public bool RowShowQty { get; set; } = true;
        public bool RowShowUnitPrice { get; set; } = true;
        public bool RowShowLineDiscount { get; set; } = true;
        public bool RowShowLineTotal { get; set; } = true;

        public bool TotalsShowTaxes { get; set; } = true;
        public bool TotalsShowDiscounts { get; set; } = true;
        public bool TotalsShowOtherExpenses { get; set; } = true;
        public bool TotalsShowGrandTotal { get; set; } = true;
        public bool TotalsShowPaymentRecv { get; set; } = true;
        public bool TotalsShowBalance { get; set; } = true;

        public bool ShowQr { get; set; } = false;
        public bool ShowCustomerOnReceipt { get; set; } = true;
        public bool ShowCashierOnReceipt { get; set; } = true;
        public bool PrintBarcodeOnReceipt { get; set; } = false;

        // Localizable text (simple version)
        public string? HeaderText { get; set; }
        public string? FooterText { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Outlet? Outlet { get; set; }
    }
}
