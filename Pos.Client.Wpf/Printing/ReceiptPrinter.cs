// Pos.Client.Wpf/Printing/ReceiptPrinter.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Pos.Client.Wpf.Models;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Models.Settings;
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPrinter
    {
        // Fallbacks if settings not provided
        public static string DefaultPrinterName = "POS80";
        public static string DefaultStoreName = "My Store";

        // ---------------- Back-compat overloads (sync) ----------------

        // Old 2-arg
        public static void PrintSale(Sale sale, IEnumerable<CartLine> cart)
        {
            // Use defaults
            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till: null,
                storeName: DefaultStoreName,
                cashierName: "Cashier",
                salesmanName: null,
                eReceiptBaseUrl: null
            );
            RawPrinterHelper.SendBytesToPrinter(DefaultPrinterName, bytes);
        }

        // Current 5-arg you’re calling from the view
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
                storeName: "One Dollar Shop",   // previous hard-coded value preserved
                cashierName: cashierName,
                salesmanName: salesmanName,
                eReceiptBaseUrl: null
            );
            RawPrinterHelper.SendBytesToPrinter(DefaultPrinterName, bytes);
        }

        // Richest sync overload if you want to supply a store name explicitly
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
            RawPrinterHelper.SendBytesToPrinter(DefaultPrinterName, bytes);
        }

        // ---------------- New async overload using InvoiceSettingsDto ----------------

        public static async Task PrintSaleAsync(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string cashierName,
            string? salesmanName,
            InvoiceSettingsDto settings)
        {
            // Map settings → values used by the builder/printer
            var printerName = settings?.PrinterName ?? DefaultPrinterName;
            var storeName = string.IsNullOrWhiteSpace(settings?.FooterText)
                ? // if you store footer as footer only, keep the old display name fallback:
                  (string.IsNullOrWhiteSpace(settings?.PrinterName) ? DefaultStoreName : DefaultStoreName)
                : // you may prefer to keep OutletDisplayName in DTO later; for now keep old behavior:
                  DefaultStoreName;

            // NOTE: If your EscPosReceiptBuilder supports paper width or footer,
            // extend its Build(...) to accept them. For now we keep current signature.
            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till: till,
                storeName: storeName,
                cashierName: cashierName,
                salesmanName: salesmanName,
                eReceiptBaseUrl: null
            );

            // If you need to inject footer text into the ticket, add it inside EscPosReceiptBuilder.Build(...)
            // using the 'settings.FooterText' value.

            RawPrinterHelper.SendBytesToPrinter(printerName, bytes);

            // match async signature
            await Task.CompletedTask;
        }

        // ---------------- Optional specialized stubs ----------------

        public static void PrintSaleAmended(Sale amended, IEnumerable<CartLine> cart, string revisionLabel)
        {
            // TODO: If you have a builder variant for amended receipts, call it here.
            // var bytes = EscPosReceiptBuilder.BuildAmended(...);
            // RawPrinterHelper.SendBytesToPrinter(DefaultPrinterName, bytes);
        }

        public static void PrintReturn(Sale ret, IEnumerable<CartLine> lines)
        {
            // TODO: If you have a builder variant for returns, call it here.
        }

        // ---------------- Optional internal core (kept minimal) ----------------
        // If you later extend to pass paper width / footer explicitly, add a core like this:

        private static Task PrintSaleCoreAsync(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string cashierName,
            string? salesmanName,
            string printerName,
            int? paperWidthMm,
            string? footerText,
            string? storeNameOverride = null)
        {
            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till: till,
                storeName: storeNameOverride ?? DefaultStoreName,
                cashierName: cashierName,
                salesmanName: salesmanName,
                eReceiptBaseUrl: null
            );

            // If EscPosReceiptBuilder later supports paper width or footer,
            // pass them above and remove this comment.

            RawPrinterHelper.SendBytesToPrinter(printerName ?? DefaultPrinterName, bytes);
            return Task.CompletedTask;
        }

        public static Task PrintAsync(
    ReceiptDocType docType,
    ReceiptTemplate tpl,
    Sale? sale = null,
    List<CartLine>? cart = null,
    TillSession? till = null,
    Voucher? voucher = null,
    ZReportModel? z = null,
    string? storeNameOverride = null,
    string? cashierName = null,
    string? salesmanName = null)
        {
            // Choose builder based on docType
            byte[] bytes = docType switch
            {
                ReceiptDocType.Sale => EscPosReceiptBuilder.Build(
                    sale!, cart!, till,
                    storeName: storeNameOverride ?? (tpl.OutletDisplayName ?? DefaultStoreName),
                    cashierName: cashierName,
                    salesmanName: salesmanName,
                    eReceiptBaseUrl: null
                ),

                // BEFORE (causing CS7036)
                // ReceiptDocType.SaleReturn => SaleReturnReceiptBuilder.Build(/* same pattern */),

                // AFTER: reuse sale builder for now
                ReceiptDocType.SaleReturn => EscPosReceiptBuilder.Build(
                    sale!, cart!, till,
                    storeName: storeNameOverride ?? (tpl.OutletDisplayName ?? DefaultStoreName),
                    cashierName: cashierName,
                    salesmanName: salesmanName,
                    eReceiptBaseUrl: null
                ),

                ReceiptDocType.Voucher => VoucherReceiptBuilder.Build(voucher!, tpl),
                ReceiptDocType.ZReport => ZReportReceiptBuilder.Build(z!, tpl),
                _ => throw new NotSupportedException(docType.ToString())
            };


            RawPrinterHelper.SendBytesToPrinter(tpl.PrinterName ?? DefaultPrinterName, bytes);
            return Task.CompletedTask;
        }

    }
}
