// Pos.Domain/Entities/ReceiptTemplate.cs
using System;

namespace Pos.Domain.Entities
{
    public class ReceiptTemplate
    {
        public int Id { get; set; }
        public int? OutletId { get; set; }          // null => Global/default
        public ReceiptDocType DocType { get; set; } // Sale, SaleReturn, Voucher, ZReport

        // ---------- Printer & page (kept here) ----------
        public int PaperWidthMm { get; set; } = 80;     // 58 or 80
        public bool EnableDrawerKick { get; set; } = true;

        // ---------- Logo (coming from Identity & Branding for content; here we only keep presentation) ----------
        public bool ShowLogoOnReceipt { get; set; } = true;
        public int LogoMaxWidthPx { get; set; } = 384;  // ESC/POS 80mm full width ~ 576; 384 keeps it moderate
        public string LogoAlignment { get; set; } = "Center"; // "Left" | "Center" | "Right"

        // ---------- Row visibility ----------
        public bool RowShowProductName { get; set; } = true;
        public bool RowShowProductSku { get; set; } = false;
        public bool RowShowQty { get; set; } = true;
        public bool RowShowUnitPrice { get; set; } = true;
        public bool RowShowLineDiscount { get; set; } = true;
        public bool RowShowLineTotal { get; set; } = true;

        // ---------- Totals visibility ----------
        public bool TotalsShowTaxes { get; set; } = true;
        public bool TotalsShowDiscounts { get; set; } = true;
        public bool TotalsShowOtherExpenses { get; set; } = true;
        public bool TotalsShowGrandTotal { get; set; } = true;
        public bool TotalsShowPaymentRecv { get; set; } = true;
        public bool TotalsShowBalance { get; set; } = true;

        // ---------- Common sales section ----------
        public bool ShowQr { get; set; } = false;                 // generic QR (e.g., e-receipt URL)
        public bool ShowCustomerOnReceipt { get; set; } = true;
        public bool ShowCashierOnReceipt { get; set; } = true;
        public bool PrintBarcodeOnReceipt { get; set; } = false;

        // ---------- New (from your requirements) ----------
        // NOTE: Content (NTN, FBR enable, FBR QR/logo) comes from General/Identity settings at runtime.
        // These booleans only toggle whether we should show them if available.
        public bool ShowNtnOnReceipt { get; set; } = true;        // Render NTN if it exists in Identity/General settings
        public bool ShowFbrOnReceipt { get; set; } = true;        // Render FBR QR/logo if FBR mode is enabled in General settings

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        // Nav
        public Outlet? Outlet { get; set; }
    }
}
