//Pos.Client.Wpf/Printing/ReceiptPrinter
using System.Collections.Generic;
using Pos.Domain.Entities;
using Pos.Client.Wpf.Models;
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // <-- to see CartLine from MainWindow namespace
using System.Threading.Tasks;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPrinter
    {
        public static string PrinterName = "POS80";

        // Back-compat 2-arg
        public static void PrintSale(Sale sale, IEnumerable<CartLine> cart)
        {
            var bytes = EscPosReceiptBuilder.Build(sale, cart, "My Store");
            RawPrinterHelper.SendBytesToPrinter(PrinterName, bytes);
        }


        // NEW: matches your current call site (5 args)
        public static void PrintSale(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string cashierName,
            string? salesmanName
        )
        {
            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till: till,
                storeName: "One Dollar Shop",
                cashierName: cashierName,
                salesmanName: salesmanName,
                eReceiptBaseUrl: null
            );
            RawPrinterHelper.SendBytesToPrinter(PrinterName, bytes);
        }

        // (Optional) richest overload if you ever want to pass store/URL dynamically
        public static void PrintSale(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string storeName,
            string cashierName,
            string? salesmanName,
            string? eReceiptBaseUrl = null
        )
        {
            var bytes = EscPosReceiptBuilder.Build(
                sale, cart, till, storeName, cashierName, salesmanName, eReceiptBaseUrl
            );
            RawPrinterHelper.SendBytesToPrinter(PrinterName, bytes);
        }

        public static void PrintSaleAmended(Sale amended, IEnumerable<CartLine> cart, string revisionLabel)
        {
            // Reuse your EscPosReceiptBuilder; add "(Rev n)" next to invoice number.
            // e.g., EscPosReceiptBuilder.BuildAmended(...)
        }

        public static void PrintReturn(Sale ret, IEnumerable<CartLine> lines)
        {
            // Build “Return Receipt”: show Original Invoice (if any),
            // list returned lines and refund/payment method.
        }

        public static async Task PrintSaleAsync(
        Sale sale,
        IEnumerable<CartLine> cart,
        TillSession? till,
        string cashierName,
        string? salesmanName,
        Pos.Domain.Services.IInvoiceSettingsService settingsSvc)
        {
            var result = await settingsSvc.GetAsync(sale.OutletId, "en");
            var settings = result.Settings;
            // var loc = result.Local; // keep for future localization work

            // Choose store display name from settings (fallback)
            var storeName = string.IsNullOrWhiteSpace(settings.OutletDisplayName) ? "My Store" : settings.OutletDisplayName;

            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till,
                storeName,          // 4th param is string storeName
                cashierName,
                salesmanName,
                eReceiptBaseUrl: null);

            RawPrinterHelper.SendBytesToPrinter(settings.PrinterName ?? PrinterName, bytes);
        }


    }
}
