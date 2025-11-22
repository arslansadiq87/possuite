// Pos.Client.Wpf/Printing/ReceiptContentBuilder.cs
using System;
using System.Collections.Generic;
using System.Text;
using Pos.Client.Wpf.Models;
using Pos.Domain.Entities;
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine
using Pos.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptContentBuilder
    {
        /// <summary>
        /// Builds the full ESC/POS-ready plain text for SALE receipts.
        /// NOTE:
        ///  - This method now PREPENDS an ESC/POS-styled Business Name (big/bold/centered),
        ///    and asks the internal text builder NOT to render the business name again.
        ///  - This method also SUPPRESSES the text "logo" placeholder because bitmap logo
        ///    is added separately in the printer pipeline (ReceiptPrinter.PrintAsync).
        /// </summary>
        public static string BuildSaleText(
            ReceiptTemplate tpl,
            IdentitySettings identity,
            IEnumerable<CartLine> cart,
            Sale saleHeader,
            string cashierName,
            string? salesmanName)
        {
            // Columns according to paper width
            // For 58mm use 32 cols; for 80mm use 42 cols
            int widthCols = (tpl.PaperWidthMm <= 58) ? 32 : 42;

            string businessName = identity.OutletDisplayName ?? string.Empty;
            string address = CenterEachLine(JoinNonEmpty("\n", identity.AddressLine1, identity.AddressLine2), widthCols);
            string contacts = CenterEachLine(JoinNonEmpty("  ", identity.Phone), widthCols);


            // Optional NTN
            string? ntnToShow = (tpl.ShowNtnOnReceipt && !string.IsNullOrWhiteSpace(identity.BusinessNtn))
                ? identity.BusinessNtn!
                : null;

            // FBR toggles resolved from Identity (content) + Template (visibility)
            bool enableFbr = identity.EnableFbr && tpl.ShowFbrOnReceipt;
            bool showFbrQr = enableFbr;
            string? fbrPosId = enableFbr ? identity.FbrPosId : null;

            // Footer (scoped setting usually replaces this at call site if needed)
            // Footer: prefer scoped FooterSale; fallback to default when blank
            // Footer: outlet-scoped -> global -> default
            var footer = ResolveSaleFooter(saleHeader?.OutletId);



            // Build item lines for the ASCII table
            var previewLines = BuildLinesFromCart(cart);

            // Build the "body" text via preview builder BUT:
            //  - Do NOT render business name here (we will prepend an ESC/POS-styled BN).
            //  - Do NOT render a text "logo" placeholder (bitmap logo is handled in printer).
            string bodyText = ReceiptPreviewBuilder.BuildText(
                width: widthCols,
                topMarginLines: tpl.TopMarginLines,       // preview builder may add leading blanks; OK
                businessName: string.Empty,               // suppress BN here
                showBusinessName: false,                  // ← IMPORTANT
                businessNameBold: tpl.BusinessNameBold,   // (not used because showBusinessName=false)
                businessNameFontSizePt: tpl.BusinessNameFontSizePt,
                addressBlock: address,
                showAddress: tpl.ShowAddress,
                contacts: contacts,
                showContacts: tpl.ShowContacts,
                receiptTypeCaption: "SALE INVOICE",
                businessNtn: ntnToShow,
                showLogo: false,                          // ← suppress logo placeholder in text
                showCustomer: tpl.ShowCustomerOnReceipt,
                showCashier: tpl.ShowCashierOnReceipt,
                // row flags
                showName: tpl.RowShowProductName,
                showSku: tpl.RowShowProductSku,
                showQty: tpl.RowShowQty,
                showUnit: tpl.RowShowUnitPrice,
                showLineDisc: tpl.RowShowLineDiscount,
                showLineTotal: tpl.RowShowLineTotal,
                // totals flags
                showTax: tpl.TotalsShowTaxes,
                showInvDisc: tpl.TotalsShowDiscounts,
                showOtherExp: tpl.TotalsShowOtherExpenses,
                showGrand: tpl.TotalsShowGrandTotal,
                showPaid: tpl.TotalsShowPaymentRecv,
                showBalance: tpl.TotalsShowBalance,
                // footer & FBR
                footer: footer,
                enableFbr: enableFbr,
                showFbrQr: showFbrQr,
                fbrPosId: fbrPosId,
                // data
                lines: previewLines,
                sale: new ReceiptPreviewSale
                {
                    // ---- Header / meta ----
                    InvoiceNumber = saleHeader?.InvoiceNumber ?? saleHeader?.Id,
                    Ts = saleHeader?.Ts ?? DateTime.Now,
                    // Show outlet name on the right (the preview uses OutletCode for that slot)
                    OutletId = saleHeader?.OutletId,
                    OutletCode = identity?.OutletDisplayName,   // fixes "Outlet N/A"
                    // Show "Counter {id}" (preview will format this nicely)
                    CounterId = saleHeader?.CounterId,
                    CustomerName = (saleHeader != null && saleHeader.CustomerKind == Pos.Domain.CustomerKind.WalkIn)
                   ? "Walk-in Customer"
                   : null, // (if you later pass a registered customer's name, it will render here)
                    CashierName = cashierName,        // from logged-in user (SaleInvoiceView already passes it)
                    SalesmanName = salesmanName,
                    IsReturn = saleHeader?.IsReturn ?? false,
                    // Use persisted figures so printed totals match accounting:
                    Subtotal = saleHeader?.Subtotal ?? 0m,
                    InvoiceDiscount = saleHeader?.InvoiceDiscountValue ?? 0m,  // your field
                    Tax = saleHeader?.TaxTotal ?? 0m,
                    OtherExpenses = 0m,                                 // (no field in Sale; keep 0 unless you add one)
                    Total = saleHeader?.Total ?? 0m,
                    // Paid = cash + card (per your model)
                    Paid = (saleHeader?.CashAmount ?? 0m) + (saleHeader?.CardAmount ?? 0m),
                    // (ReceiptPreviewBuilder already does: if Balance==0 then max(0, total-paid))
                    Balance = 0m
                },
                // barcode / qr toggles
                showBarcodeOnReceipt: tpl.PrintBarcodeOnReceipt,

                showGenericQr: tpl.ShowQr
            );

            // If Business Name is enabled & non-empty, prepend an ESC/POS-styled BN line
            var sb = new StringBuilder();
            if (tpl.ShowBusinessName && !string.IsNullOrWhiteSpace(businessName))
            {
                sb.Append(BuildBusinessNameEsc(businessName, tpl));
            }

            static string CenterEachLine(string text, int widthCols)
            {
                if (string.IsNullOrEmpty(text)) return text;
                var lines = text.Replace("\r\n", "\n").Split('\n');
                var sb = new StringBuilder();
                foreach (var l in lines)
                {
                    var s = l?.Trim() ?? "";
                    if (s.Length >= widthCols) { sb.AppendLine(s.Length > widthCols ? s[..widthCols] : s); continue; }
                    int pad = Math.Max(0, (widthCols - s.Length) / 2);
                    sb.AppendLine(new string(' ', pad) + s);
                }
                return sb.ToString().TrimEnd('\n');
            }

            // Append the rest of the body (address/contacts, items, totals, footer, etc.)
            sb.Append(bodyText);

            // Final text goes to EscPosTextEncoder.FromPlainText(...) upstream
            return sb.ToString();
        }

        /// <summary>
        /// ESC/POS styling for the business name line:
        /// - Center just this line
        /// - Apply bold if template says so
        /// - Map BusinessNameFontSizePt to ESC @ '!' mode:
        ///     >= 24pt → 0x30 (double width + double height)
        ///     >= 18pt → 0x10 (double height)
        ///     else    → 0x00 (normal)
        /// - Reset styles and left alignment after this line.
        /// </summary>
        private static string BuildBusinessNameEsc(string name, ReceiptTemplate tpl)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Bold on/off
            const string BoldOn = "\x1B" + "E" + "\x01";
            const string BoldOff = "\x1B" + "E" + "\x00";

            // Print mode (double height/width)
            byte mode = 0x00; // normal
            if (tpl.BusinessNameFontSizePt.HasValue && tpl.BusinessNameFontSizePt.Value >= 24)
                mode = 0x30; // double width + double height
            else if (tpl.BusinessNameFontSizePt.HasValue && tpl.BusinessNameFontSizePt.Value >= 18)
                mode = 0x10; // double height

            string ModeOn = "\x1B" + "!" + (char)mode;
            const string ModeOff = "\x1B" + "!" + "\x00";

            // Align center for BN, then reset to left
            const string Center = "\x1B" + "a" + "\x01";
            const string Left = "\x1B" + "a" + "\x00";
            // ESC M n  →  n=0 Font A, n=1 Font B (some models also support 2=Font C)
            const string FontA = "\x1B" + "M" + "\x00";
            const string FontB = "\x1B" + "M" + "\x01";
            bool useFontBForBusinessName = true; // ← set true for Font B, false for Font A


            var sb = new StringBuilder();
            sb.Append(Center);
            sb.Append(useFontBForBusinessName ? FontB : FontA);

            sb.Append(ModeOn);
            if (tpl.BusinessNameBold) sb.Append(BoldOn);

            sb.Append(name).Append('\n');

            if (tpl.BusinessNameBold) sb.Append(BoldOff);
            sb.Append(ModeOff);
            sb.Append(FontA); // reset to default font so the rest of the receipt is unchanged
            sb.Append(Left);

            return sb.ToString();
        }

        private static string JoinNonEmpty(string sep, params string?[] parts)
        {
            var list = new List<string>();
            foreach (var p in parts)
                if (!string.IsNullOrWhiteSpace(p))
                    list.Add(p!.Trim());
            return string.Join(sep, list);
        }

        /// <summary>
        /// Builds preview rows from the live cart, computing net/discount carefully:
        /// - Prefer persisted LineNet if available,
        /// - Else derive from per-unit UnitNet,
        /// - Else apply DiscountPct/DiscountAmt,
        /// - Else fall back to gross (UnitPrice * Qty).
        /// </summary>
        private static List<ReceiptPreviewLine> BuildLinesFromCart(IEnumerable<CartLine>? cart)
        {
            var list = new List<ReceiptPreviewLine>();
            if (cart == null) return list;

            foreach (var c in cart)
            {
                var qty = (decimal)c.Qty;
                var lineGross = c.UnitPrice * qty;

                // 1) Most authoritative: stored final net
                decimal lineNet = c.LineNet;

                // 2) Else derive from per-unit net
                if (lineNet <= 0m && c.UnitNet > 0m)
                    lineNet = c.UnitNet * qty;

                // 3) Else derive from discounts on the line
                if (lineNet <= 0m)
                {
                    if (c.DiscountPct.HasValue && c.DiscountPct.Value > 0m)
                    {
                        var unitAfterPct = c.UnitPrice * (1m - (c.DiscountPct.Value / 100m));
                        lineNet = unitAfterPct * qty;
                    }
                    else if (c.DiscountAmt.HasValue && c.DiscountAmt.Value > 0m)
                    {
                        // Treat DiscountAmt as per-unit amount off (consistent with SaleLine)
                        var unitAfterAmt = c.UnitPrice - c.DiscountAmt.Value;
                        lineNet = unitAfterAmt * qty;
                    }
                    else
                    {
                        // 4) No discount → net == gross
                        lineNet = lineGross;
                    }
                }

                var lineDiscount = Math.Max(0m, lineGross - lineNet);

                list.Add(new ReceiptPreviewLine
                {
                    Name = c.DisplayName,
                    Sku = c.Sku,
                    Qty = c.Qty,         // int
                    Unit = c.UnitPrice,   // show list/unit price column
                    LineDiscount = lineDiscount   // LineTotal = Qty*Unit - LineDiscount (in model)
                });
            }

            return list;
        }

        private static string ResolveSaleFooter(int? outletId)
        {
            const string DefaultFooter = "Thank you for shopping with us!";

            try
            {
                var scopedSvc = App.Services.GetRequiredService<IInvoiceSettingsScopedService>();

                // 1) Outlet-scoped footer (if outletId present and footer is non-empty)
                if (outletId.HasValue && outletId.Value > 0)
                {
                    var outletScoped = scopedSvc.GetForOutletAsync(outletId.Value, CancellationToken.None)
                                                .GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(outletScoped?.FooterSale))
                        return outletScoped!.FooterSale!.Trim();
                }

                // 2) Global footer (if non-empty)
                var global = scopedSvc.GetGlobalAsync(CancellationToken.None)
                                      .GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(global?.FooterSale))
                    return global!.FooterSale!.Trim();

                // 3) Default
                return DefaultFooter;
            }
            catch
            {
                // Any resolution error -> default footer
                return DefaultFooter;
            }
        }

    }
}
