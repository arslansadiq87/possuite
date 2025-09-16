//Pos.Client.Wpf/Printing/ReceiptPrinter
using System.Collections.Generic;
using Pos.Domain.Entities;
using Pos.Client.Wpf.Models;

using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceWindow; // <-- to see CartLine from MainWindow namespace

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPrinter
    {
        public static string PrinterName = "POS80";

        // Back-compat 2-arg
        public static void PrintSale(Sale sale, IEnumerable<CartLine> cart)
        {
            var bytes = EscPosReceiptBuilder.Build(sale, cart, storeName: "One Dollar Shop");
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


    }
}
