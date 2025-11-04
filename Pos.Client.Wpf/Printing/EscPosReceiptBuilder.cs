//Pos.Client.Wpf/Printing/EscPosReceiptBuilder
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pos.Domain;
using Pos.Domain.Entities;
//using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceWindow; // makes CartLine visible here
using Pos.Client.Wpf.Models;

namespace Pos.Client.Wpf.Printing
{
    public static class EscPosReceiptBuilder
    {
        // Change to 32 for 58mm printers (default 42 is for 80mm paper)
        private const int WIDTH = 42;

        // Most ESC/POS printers use CP437/850. 437 covers ASCII.
        private static readonly Encoding Enc = Encoding.GetEncoding(437);

        // ---- Back-compat overload (old signature still works) ----
        public static byte[] Build(Sale sale, IEnumerable<CartLine> cart, string storeName = "My Store")
            => Build(sale, cart, till: null, storeName, cashierName: "", salesmanName: null, eReceiptBaseUrl: null);

        // ---- New rich overload ----
        public static byte[] Build(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string storeName,
            string cashierName,
            string? salesmanName,
            string? eReceiptBaseUrl = null)
        {
            var bytes = new List<byte>();

            // Keep ONLY one Add with params to avoid CS0128
            void Add(params byte[] b) => bytes.AddRange(b);
            // When you need IEnumerable<byte>, call bytes.AddRange(...) directly.
            void Txt(string s) => bytes.AddRange(Enc.GetBytes(s));

            // ESC/POS helpers
            byte ESC = 0x1B, GS = 0x1D;

            void Initialize() => Add(ESC, (byte)'@');                 // ESC @
            void Align(byte n) => Add(ESC, (byte)'a', n);             // 0=left 1=center 2=right
            void Bold(bool on) => Add(ESC, (byte)'E', (byte)(on ? 1 : 0));
            void Underline(bool on) => Add(ESC, (byte)'-', (byte)(on ? 1 : 0));
            void Feed(int n = 1) => Add(ESC, (byte)'d', (byte)n);
            void Cut() => Add(GS, (byte)'V', 0x41, 0x03);             // full cut after 3 feed
            // void DrawerKick() => Add(ESC, (byte)'p', 0x00, 0x19, 0xFA); // if needed

            string Line(string left, string right, int width = WIDTH)
            {
                left ??= ""; right ??= "";
                if (left.Length + right.Length >= width) return left + "\n";
                return left + new string(' ', width - left.Length - right.Length) + right + "\n";
            }
            string Repeat(char c, int n) => new string(c, n) + "\n";
            static string Money(decimal d) => d.ToString("0.00");

            // QR (Model 2) — many printers support this; if not, we still print the URL/text.
            void PrintQr(string data)
            {
                var d = Enc.GetBytes(data);
                // Store data
                int len = d.Length + 3;
                byte pL = (byte)(len % 256);
                byte pH = (byte)(len / 256);
                Add(GS, 0x28, 0x6B, pL, pH, 0x31, 0x50, 0x30);
                bytes.AddRange(d); // <— use AddRange here (we removed the IEnumerable Add overload)

                // Select model 2
                Add(GS, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x41, 0x32);
                // Size (1..16)
                Add(GS, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06);
                // Error correction (48=L,49=M,50=Q,51=H)
                Add(GS, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31); // M
                // Print
                Add(GS, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30);
            }

            // ===== Build receipt =====
            Initialize();

            // Header
            Align(1); Bold(true); Txt(storeName + "\n"); Bold(false);
            var invText = (sale.CounterId > 0 && sale.InvoiceNumber > 0)
                ? $"Invoice: {sale.CounterId}-{sale.InvoiceNumber}\n"
                : $"Sale ID: {sale.Id}\n";
            Txt(invText);

            var tsLocal = DateTime.Now;
            // If you store sale.Ts in UTC, prefer that:
            // tsLocal = sale.Ts.Kind == DateTimeKind.Utc ? sale.Ts.ToLocalTime() : sale.Ts;
            //Txt($"{tsLocal:yyyy-MM-dd HH:mm}\n");
            // Use UTC from the entity and convert to user display zone
            var tsText = Pos.Client.Wpf.Services.TimeService.Format(
                sale.Ts, "yyyy-MM-dd HH:mm");  // if sale.Ts is stored UTC; if not, wrap with SpecifyKind(UTC)
            Txt($"{tsText}\n");

            if (till != null)
            {
                var openedText = Pos.Client.Wpf.Services.TimeService.Format(
                    till.OpenTs, "HH:mm");      // same assumption about UTC storage
                Txt($"Till: {till.Id}   Opened: {openedText}\n");
            }
            if (sale.IsReturn) { Underline(true); Txt("** RETURN / REFUND **\n"); Underline(false); }
            Feed();

            Align(0);
            // People & customer
            if (!string.IsNullOrWhiteSpace(cashierName)) Txt(Line("Cashier", cashierName));
            if (!string.IsNullOrWhiteSpace(salesmanName)) Txt(Line("Salesman", salesmanName));
            var customerLabel = sale.CustomerKind == CustomerKind.WalkIn
                ? "Walk-in"
                : $"{(sale.CustomerName ?? "").Trim()} {(string.IsNullOrWhiteSpace(sale.CustomerPhone) ? "" : $"({sale.CustomerPhone})")}".Trim();
            Txt(Line("Customer", customerLabel));
            Txt(Repeat('-', WIDTH));

            // Items
            foreach (var l in cart)
            {
                // name line
                var name = l.DisplayName ?? "";
                if (name.Length > WIDTH - 10) name = name[..(WIDTH - 10)]; // keep space for qty/price
                Txt(Line($"{name} x{l.Qty}", Money(l.LineTotal)));
                // unit/price line
                Txt(Line($"   @{Money(l.UnitPrice)}", ""));
            }

            Txt(Repeat('-', WIDTH));

            // Totals (use the values you saved on Sale)
            if (sale.Subtotal != 0m) Txt(Line("Subtotal", Money(sale.Subtotal)));

            // Remove the '?? 0m' — InvoiceDiscountValue is non-nullable decimal in your model
            if (sale.InvoiceDiscountValue > 0m)
                Txt(Line("Invoice Discount", "-" + Money(sale.InvoiceDiscountValue)));

            if (sale.TaxTotal != 0m) Txt(Line("Tax", Money(sale.TaxTotal)));

            Txt(Line("TOTAL", Money(sale.Total)));
            Feed();

            // Payment breakdown & change
            var paid = sale.CashAmount + sale.CardAmount;
            var change = Math.Max(0m, paid - sale.Total);
            Txt(Line("Payment", sale.PaymentMethod.ToString()));
            if (sale.CashAmount > 0) Txt(Line("  Cash", Money(sale.CashAmount)));
            if (sale.CardAmount > 0) Txt(Line("  Card", Money(sale.CardAmount)));
            Txt(Line("Received", Money(paid)));
            if (change > 0) Txt(Line("Change", Money(change)));

            Feed();

            // E-receipt QR + link text
            Align(1);
            string linkOrToken;
            if (!string.IsNullOrWhiteSpace(eReceiptBaseUrl))
                linkOrToken = eReceiptBaseUrl!.TrimEnd('/') + "/" + sale.EReceiptToken;
            else
                linkOrToken = "E-Receipt: " + sale.EReceiptToken;

            Txt("E-Receipt\n");
            try { PrintQr(linkOrToken); Feed(); } catch { /* printer may not support QR */ }
            Txt(linkOrToken + "\n");

            Feed();

            // Footer
            if (!string.IsNullOrWhiteSpace(sale.InvoiceFooter))
                Txt(sale.InvoiceFooter + "\n");
            else
                Txt("Thank you!\n");

            Feed(2);
            Cut();
            // DrawerKick(); // uncomment if you trigger cash drawer via printer pulse

            return bytes.ToArray();
        }
    }
}
