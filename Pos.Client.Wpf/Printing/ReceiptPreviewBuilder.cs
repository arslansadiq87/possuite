// Pos.Client.Wpf/Printing/ReceiptPreviewBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Documents;
using System.Windows;

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPreviewBuilder
    {
        /// <summary>
        /// Builds a monospace text preview of a receipt that fits 58mm (~32 cols) and 80mm (~42 cols).
        /// - 58mm prints integers (no decimals)
        /// - 80mm prints with 2 decimals
        /// - Item table columns are fixed and aligned: Name | Qty | Price | Total
        /// - Discount appears on a separate left-indented line under the item name
        /// - Totals section uses two right-anchored columns: [Label][space][Value]
        /// </summary>
        /// var fd = new FlowDocument();

        public static string BuildText(
    int width,
    int topMarginLines,                  // NEW
    string? businessName,
    bool showBusinessName,               // NEW
    bool businessNameBold,               // NEW
    int? businessNameFontSizePt,         // NEW (text preview sim only)
    string? addressBlock,
    bool showAddress,                    // NEW
    string? contacts,
    bool showContacts,                   // NEW
    string? businessNtn,
    string receiptTypeCaption,           // NEW: "SALE INVOICE", etc. (non-nullable)
    bool showLogo,
    bool showCustomer,                   // toggles
    bool showCashier,
    bool showName, bool showSku, bool showQty, bool showUnit, bool showLineDisc, bool showLineTotal,
    // totals flags
    bool showTax, bool showInvDisc, bool showOtherExp, bool showGrand, bool showPaid, bool showBalance,
    // footer
    string? footer,
    // FBR
    bool enableFbr, bool showFbrQr, string? fbrPosId,
    // data
    IReadOnlyList<ReceiptPreviewLine>? lines,   // nullable
    ReceiptPreviewSale? sale,                   // nullable
                                                // generic barcode / QR preview
    bool showBarcodeOnReceipt,
    bool showGenericQr)
        {
            var sb = new StringBuilder();

            // ---------------- helpers ----------------
            // Trim left text if needed so right text is always visible.
            string Line(string left, string right)
            {
                left ??= ""; right ??= "";
                int space = width - right.Length; if (space < 1) space = 1;
                if (left.Length > space) left = left[..space];
                return left + new string(' ', Math.Max(0, width - left.Length - right.Length)) + right + "\n";
            }

            string Center(string s)
                => s.Length >= width ? s[..width] + "\n"
                                     : new string(' ', Math.Max(0, (width - s.Length) / 2)) + s + "\n";

            string Repeat(char c) => new string(c, width) + "\n";

            static string ClipLeft(string s, int max)
                => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

            static string ClipRight(string s, int max)
                => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[^max..]);

            // 58mm (<=34 cols) shows ints; 80mm shows 0.00
            bool noDecimals = width <= 34;
            string Money(decimal d) => noDecimals ? Math.Round(d).ToString("0") : d.ToString("0.00");

            // Normalize nullable inputs
            lines ??= Array.Empty<ReceiptPreviewLine>();
            bool hasSale = sale is not null;

            string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
            var business = Safe(businessName);
            var address = Safe(addressBlock);
            var contact = Safe(contacts);
            var ntn = Safe(businessNtn);
            var typeCap = Safe(receiptTypeCaption); // receiptTypeCaption is non-nullable by signature; Safe stops accidental spaces

            // Top margin
            if (topMarginLines > 0) sb.Append(new string('\n', topMarginLines));

            // ---------------- header ----------------
            //if (showLogo) sb.Append(Center("[LOGO]"));

            // Business name
            if (showBusinessName && business.Length > 0)
            {
                var text = business;
                // Simulate bold/size for preview (ESC/POS does real styling)
                if (businessNameBold) text = text.ToUpperInvariant();
                sb.Append(Center(text));
            }

            // Address / contacts
            if (showAddress && address.Length > 0)
            {
                foreach (var ln in address.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)))
                    sb.Append(Center(ln.Trim()));
            }
            if (showContacts && contact.Length > 0)
                sb.Append(Center(contact));
            if (ntn.Length > 0)
                sb.Append(Center("NTN: " + ntn));

            // ==== RECEIPT TYPE ====
            if (!string.IsNullOrWhiteSpace(typeCap))
            {
                sb.Append(Repeat('='));
                sb.Append(Center(typeCap));
                sb.Append(Repeat('='));
            }

            // ---------------- sale-only header bits ----------------
            if (hasSale)
            {
                // Invoice (left)  Date (right)
                // Invoice (left)  Date (right)
                string invLeft;
                {
                    // Convert.ToString handles nullable numerics
                    var invNum = Convert.ToString(sale!.InvoiceNumber);
                    invLeft = !string.IsNullOrWhiteSpace(invNum) ? $"Invoice: {invNum}" : "Invoice: N/A";
                }
                var dateRight = sale!.Ts.ToString("yyyy-MM-dd HH:mm");
                sb.Append(Line(invLeft, dateRight));

                

                // Counter (left)  Outlet (right)
                var counterLabel = !string.IsNullOrWhiteSpace(sale.CounterName)
                    ? sale.CounterName!
                    : (sale.CounterId.HasValue ? $"Counter {sale.CounterId}" : "Counter N/A");

                var outletRight = !string.IsNullOrWhiteSpace(sale.OutletCode)
                    ? sale.OutletCode!
                    : (sale.OutletId.HasValue ? $"Outlet {sale.OutletId}" : "Outlet N/A");

                sb.Append(Line($"Counter: {counterLabel}", outletRight));

                // Cashier (left) | Customer (right) — each independently controlled by its toggle
                if ((showCashier && !string.IsNullOrWhiteSpace(sale.CashierName)) ||
                    (showCustomer && !string.IsNullOrWhiteSpace(sale.CustomerName)))
                {
                    var left = (showCashier && !string.IsNullOrWhiteSpace(sale.CashierName)) ? $"Cashier: {sale.CashierName!.Trim()}" : "";
                    var right = (showCustomer && !string.IsNullOrWhiteSpace(sale.CustomerName)) ? sale.CustomerName!.Trim() : "";
                    sb.Append(Line(left, right));
                }
            }

            sb.Append(Repeat('-'));

            // ---------------- items: fixed table (sale only, and only if we have lines) ----------------
            if (hasSale && lines.Count > 0)
            {
                int nameW = (width >= 42) ? 20 : 14;
                int qtyW = 3;
                int unitW = noDecimals ? 5 : 7;
                int totalW = noDecimals ? 6 : 8;

                // recompute available widths based on visible columns
                int cols = 1
                    + (showQty ? 1 : 0)
                    + (showUnit ? 1 : 0)
                    + (showLineTotal ? 1 : 0);

                // nominal spaces between visible columns
                int gaps = Math.Max(0, cols - 1);

                // target: nameW + (qty?) + (unit?) + (total?) + gaps == width
                int visibleWidth = (showQty ? qtyW : 0) + (showUnit ? unitW : 0) + (showLineTotal ? totalW : 0) + gaps;
                nameW = Math.Max(6, width - visibleWidth);
                if (nameW + visibleWidth > width)
                    nameW = Math.Max(6, width - visibleWidth);

                string Row(string n, string q, string u, string t)
                {
                    var sbRow = new StringBuilder(width + 2);

                    // Name (always present)
                    n = string.IsNullOrEmpty(n) ? "" : (n.Length <= nameW ? n : n[..nameW]);
                    sbRow.Append(n.PadRight(nameW));

                    void AddCol(string s, int w)
                    {
                        sbRow.Append(' ');
                        s = string.IsNullOrEmpty(s) ? "" : ClipRight(s, w);
                        sbRow.Append(s.PadLeft(w));
                    }

                    if (showQty) AddCol(q, qtyW);
                    if (showUnit) AddCol(u, unitW);
                    if (showLineTotal) AddCol(t, totalW);

                    sbRow.Append('\n');
                    return sbRow.ToString();
                }

                // header row matches visible columns
                string hName = "Item";
                string hQty = showQty ? "Qty" : "";
                string hUnit = showUnit ? "Price" : "";
                string hTot = showLineTotal ? "Total" : "";

                sb.Append(Row(hName, hQty, hUnit, hTot));
                sb.Append(Repeat('-'));

                foreach (var l in lines)
                {
                    string name = showName && !string.IsNullOrWhiteSpace(l.Name) ? l.Name!
                                : showSku && !string.IsNullOrWhiteSpace(l.Sku) ? l.Sku!
                                : "";
                    string qty = showQty ? l.Qty.ToString() : "";
                    string unit = showUnit ? Money(l.Unit) : "";
                    string tot = showLineTotal ? Money(l.LineTotal) : "";

                    sb.Append(Row(name, qty, unit, tot));

                    // Discount: left, under the item name (indented and only if enabled)
                    if (showLineDisc && l.LineDiscount > 0)
                    {
                        var discLeft = "    Disc: " + Money(l.LineDiscount); // 4-space indent
                        sb.Append(ClipLeft(discLeft, width) + "\n");
                    }
                }
                sb.Append(Repeat('-'));
            }

            // ---------------- totals: right-anchored two columns (sale only) ----------------
            if (hasSale)
            {
                decimal paid = sale!.Paid;
                decimal invDisc = sale.InvoiceDiscount;
                decimal total = sale.Total;
                decimal tax = sale.Tax;
                decimal other = sale.OtherExpenses;
                decimal balance = sale.Balance != 0m ? sale.Balance : Math.Max(0m, total - paid);

                var rows = new List<(string Label, string Value)>();

                if (showInvDisc && invDisc > 0m) rows.Add(("Invoice Discount", "-" + Money(invDisc)));
                if (showTax && tax != 0m) rows.Add(("Tax", Money(tax)));
                if (showOtherExp && other != 0m) rows.Add(("Other", Money(other)));
                if (showGrand) rows.Add((">> TOTAL <<", Money(total)));
                if (showPaid) rows.Add(("Received", Money(paid)));
                if (showBalance && balance > 0m) rows.Add((">> BALANCE <<", Money(balance)));

                if (rows.Count > 0)
                {
                    int valW = Math.Max(noDecimals ? 3 : 4, rows.Max(r => r.Value.Length));
                    if (valW >= width) valW = Math.Max(3, width / 3);

                    int maxLabelLen = rows.Max(r => r.Label.Length);
                    int labW = Math.Min(maxLabelLen, width - 1 - valW);
                    labW = Math.Max(labW, Math.Min(maxLabelLen, (width >= 42 ? 18 : 16)));
                    if (labW + 1 + valW > width) labW = Math.Max(8, width - 1 - valW);

                    string RightCols(string label, string value)
                    {
                        if (label.Length > labW) label = label[..labW];
                        if (value.Length > valW) value = value[^valW..];
                        var leftPad = new string(' ', Math.Max(0, width - (labW + 1 + valW)));
                        return leftPad + label.PadLeft(labW) + " " + value.PadLeft(valW) + "\n";
                    }

                    foreach (var (Label, Value) in rows)
                        sb.Append(RightCols(Label, Value));

                    sb.Append("\n");
                }
            }

            //---------------- generic barcode / QR (sale only) ----------------
            if (showBarcodeOnReceipt && hasSale && !string.IsNullOrWhiteSpace(sale!.BarcodeText))
            {
                var payload = sale.BarcodeText!;
                sb.Append(Center($"[BARCODE: {payload}]"));
            }

            if (showGenericQr && hasSale && !string.IsNullOrWhiteSpace(sale!.QrText))
            {
                sb.Append(Center("[QR]"));
                sb.Append(Center(sale.QrText!));
                sb.Append("\n");
            }

            // ---------------- FBR block ----------------
            if (enableFbr)
            {
                if (!string.IsNullOrWhiteSpace(fbrPosId)) sb.Append(Center($"FBR POS: {fbrPosId}"));
                if (showFbrQr) sb.Append(Center("[FBR-QR]"));
                sb.Append("\n");
            }

            // footer
            if (!string.IsNullOrWhiteSpace(footer)) sb.Append(Center(footer.Trim()));

            return sb.ToString();
        }

    }
}
