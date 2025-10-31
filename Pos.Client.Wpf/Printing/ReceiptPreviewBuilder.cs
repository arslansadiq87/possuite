// Pos.Client.Wpf/Printing/ReceiptPreviewBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        public static string BuildText(
            int width,
            string? businessName,
            string? addressBlock,
            string? contacts,
            string? businessNtn,
            bool showLogo,
            // item flags
            bool showName, bool showSku, bool showQty, bool showUnit, bool showLineDisc, bool showLineTotal,
            // totals flags
            bool showTax, bool showInvDisc, bool showOtherExp, bool showGrand, bool showPaid, bool showBalance,
            // footer
            string? footer,
            // FBR
            bool enableFbr, bool showFbrQr, string? fbrPosId,
            // data
            IReadOnlyList<ReceiptPreviewLine> lines,
            ReceiptPreviewSale sale,
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

            // ---------------- header ----------------
            if (showLogo) sb.Append(Center("[LOGO]"));

            if (!string.IsNullOrWhiteSpace(businessName))
            {
                var title = businessName.Trim().ToUpperInvariant();
                sb.Append(Center(title));
                // double rule to simulate bold/bigger
                sb.Append(Repeat('='));
                sb.Append(Repeat('='));
            }

            if (!string.IsNullOrWhiteSpace(addressBlock))
            {
                foreach (var ln in addressBlock.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)))
                    sb.Append(Center(ln.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(contacts)) sb.Append(Center(contacts.Trim()));
            if (!string.IsNullOrWhiteSpace(businessNtn)) sb.Append(Center("NTN: " + businessNtn.Trim()));

            // ---------------- meta ----------------
            sb.Append(Repeat('-'));

            // Invoice (left)  Date (right)
            var invLeft = sale.InvoiceNumber.HasValue ? $"Invoice: {sale.InvoiceNumber}" : "Invoice: N/A";
            sb.Append(Line(invLeft, sale.Ts.ToString("yyyy-MM-dd HH:mm")));

            // Counter name (left)  Outlet code (right)
            var counterLabel = !string.IsNullOrWhiteSpace(sale.CounterName)
                ? sale.CounterName!
                : (sale.CounterId.HasValue ? $"Counter {sale.CounterId}" : "Counter N/A");

            var outletRight = !string.IsNullOrWhiteSpace(sale.OutletCode)
                ? sale.OutletCode!
                : (sale.OutletId.HasValue ? $"Outlet {sale.OutletId}" : "Outlet N/A");

            sb.Append(Line($"Counter: {counterLabel}", outletRight));

            // Cashier
            if (!string.IsNullOrWhiteSpace(sale.CashierName))
                sb.Append(Line("Cashier", sale.CashierName!));

            sb.Append(Repeat('-'));

            // ---------------- items: fixed table ----------------
            {
                int nameW = (width >= 42) ? 20 : 14;
                int qtyW = 3;
                int unitW = noDecimals ? 5 : 7;
                int totalW = noDecimals ? 6 : 8;

                // ensure the row fits the available width
                int used = nameW + 1 + qtyW + 1 + unitW + 1 + totalW;
                if (used > width)
                {
                    nameW -= (used - width);
                    if (nameW < 6) nameW = 6;
                }

                string Row(string n, string q, string u, string t)
                {
                    n = string.IsNullOrEmpty(n) ? "" : (n.Length <= nameW ? n : n[..nameW]);
                    q = string.IsNullOrEmpty(q) ? "" : ClipRight(q, qtyW);
                    u = string.IsNullOrEmpty(u) ? "" : ClipRight(u, unitW);
                    t = string.IsNullOrEmpty(t) ? "" : ClipRight(t, totalW);

                    return n.PadRight(nameW) + " "
                         + q.PadLeft(qtyW) + " "
                         + u.PadLeft(unitW) + " "
                         + t.PadLeft(totalW) + "\n";
                }

                // header row (preview only)
                sb.Append(Row("Item", "Qty", "Price", "Total"));
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

                    // Discount: left, under the item name (indented)
                    if (showLineDisc && l.LineDiscount > 0)
                    {
                        var discLeft = "    Disc: " + Money(l.LineDiscount); // 4-space indent
                        sb.Append(ClipLeft(discLeft, width) + "\n");
                    }
                }
                sb.Append(Repeat('-'));
            }

            // ---------------- totals: right-anchored two columns ----------------
            {
                // Build rows based on flags
                var rows = new List<(string Label, string Value)>();
                if (showInvDisc && sale.InvoiceDiscount > 0) rows.Add(("Invoice Discount", "-" + Money(sale.InvoiceDiscount)));
                if (showTax) rows.Add(("Tax", Money(sale.Tax)));
                if (showOtherExp) rows.Add(("Other", Money(sale.OtherExpenses)));
                if (showGrand) rows.Add((">> TOTAL <<", Money(sale.Total)));
                if (showPaid) rows.Add(("Received", Money(sale.Paid)));
                if (showBalance && sale.Balance > 0) rows.Add((">> BALANCE <<", Money(sale.Balance)));

                if (rows.Count > 0)
                {
                    int valW = Math.Max(noDecimals ? 3 : 4, rows.Max(r => r.Value.Length));
                    if (valW >= width) valW = Math.Max(3, width / 3);

                    int maxLabelLen = rows.Max(r => r.Label.Length);
                    int labW = Math.Min(maxLabelLen, width - 1 - valW);
                    // give labels decent room on 58mm to avoid truncation
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

            // ---------------- generic barcode / QR (non-FBR) ----------------
            if (showBarcodeOnReceipt && !string.IsNullOrWhiteSpace(sale.BarcodeText))
            {
                sb.Append(Center("[BARCODE]"));
                sb.Append(Center(sale.BarcodeText!));
                sb.Append("\n");
            }
            if (showGenericQr && !string.IsNullOrWhiteSpace(sale.QrText))
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
