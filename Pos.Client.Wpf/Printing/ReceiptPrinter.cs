// Pos.Client.Wpf/Printing/ReceiptPrinter.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Models;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Models.Settings;
using Pos.Domain.Services;
using Pos.Domain.Settings;
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPrinter
    {
        // Fallbacks if settings not provided
        public static string DefaultPrinterName = "POS80";
        public static string DefaultStoreName = "My Store";
        private static IRawPrinterService Raw => App.Services.GetRequiredService<IRawPrinterService>();
        private static IInvoiceSettingsScopedService Scoped
    => App.Services.GetRequiredService<IInvoiceSettingsScopedService>();

        // ---------------- Back-compat overloads (sync) ----------------
        // ---------- SYNC helper (so existing callers can stay sync if needed) ----------
        private static void SendEscPos(byte[] bytes)
            => Raw.SendEscPosAsync(DefaultPrinterName, bytes, CancellationToken.None)
                 .GetAwaiter().GetResult();

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
            SendEscPos(bytes);
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
            SendEscPos(bytes);
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
            SendEscPos(bytes);
        }

        // ---------------- New async overload using InvoiceSettingsDto ----------------

        public static async Task PrintSaleAsync(
    Sale sale,
    IEnumerable<CartLine> cart,
    TillSession? till,
    string cashierName,
    string? salesmanName)
        {
            // Resolve from Local + Scoped + Identity
            var (storeName, printerName, footer) = await ResolveRuntimeAsync();

            var bytes = EscPosReceiptBuilder.Build(
                sale,
                cart,
                till: till,
                storeName: storeName,
                cashierName: cashierName,
                salesmanName: salesmanName,
                eReceiptBaseUrl: null
            );

            await Raw.SendEscPosAsync(printerName, bytes, CancellationToken.None);
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

            SendEscPos(bytes);
            return Task.CompletedTask;
        }


        
        private static IInvoiceSettingsLocalService InvoiceLocal
            => App.Services.GetRequiredService<IInvoiceSettingsLocalService>();

        private static IIdentitySettingsService Identity
            => App.Services.GetRequiredService<IIdentitySettingsService>();

        private static ITerminalContext Ctx
            => App.Services.GetRequiredService<ITerminalContext>();

        private static async Task<(string storeName, string printerName, string footer)> ResolveRuntimeAsync()
        {
            var outletId = Ctx.OutletId;
            var counterId = Ctx.CounterId;

            var identity = await Identity.GetAsync(outletId, CancellationToken.None);

            // LOCAL --> printer only
            var local = await InvoiceLocal.GetForCounterWithFallbackAsync(counterId, CancellationToken.None);

            // SCOPED --> footer
            var scoped = await Scoped.GetForOutletAsync(outletId, CancellationToken.None);

            string store = string.IsNullOrWhiteSpace(identity?.OutletDisplayName)
                                ? DefaultStoreName
                                : identity!.OutletDisplayName!;

            string printer = string.IsNullOrWhiteSpace(local?.PrinterName)
                                ? DefaultPrinterName
                                : local!.PrinterName!;

            string footer = string.IsNullOrWhiteSpace(scoped?.FooterSale)
                                ? "Thank you for shopping with us!"
                                : scoped!.FooterSale!;

            return (store, printer, footer);
        }



        public static async Task PrintAsync(
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
            // Resolve runtime store/printer/footer from Identity & Invoice Settings
            var (storeResolved, printerResolved, footerResolved) = await ResolveRuntimeAsync();

            // Apply override for store name if provided
            var storeName = storeNameOverride ?? storeResolved;

            // Build ESC/POS bytes per document type
            byte[] bytes = docType switch
            {
                // Use Sale builder for both Sale and SaleReturn for now (layout compatible)
                ReceiptDocType.Sale => EscPosReceiptBuilder.Build(
                    sale!, cart!, till,
                    storeName: storeName,
                    cashierName: cashierName ?? "",
                    salesmanName: salesmanName,
                    eReceiptBaseUrl: null
                ),

                ReceiptDocType.SaleReturn => EscPosReceiptBuilder.Build(
                    sale!, cart!, till,
                    storeName: storeName,
                    cashierName: cashierName ?? "",
                    salesmanName: salesmanName,
                    eReceiptBaseUrl: null
                ),

                // These builders still accept tpl (template holds only builder flags now)
                ReceiptDocType.Voucher => VoucherReceiptBuilder.Build(voucher!, tpl),
                ReceiptDocType.ZReport => ZReportReceiptBuilder.Build(z!, tpl),

                _ => throw new NotSupportedException(docType.ToString())
            };

            // IMPORTANT: send to the resolved printer (not the default)
            await Raw.SendEscPosAsync(printerResolved, bytes, CancellationToken.None);
        }


    }
}
