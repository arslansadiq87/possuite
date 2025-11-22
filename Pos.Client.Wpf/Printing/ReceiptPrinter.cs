// Pos.Client.Wpf/Printing/ReceiptPrinter.cs
// ──────────────────────────────────────────────────────────────────────────────
// ReceiptPrinter – unified print pipeline (ESC/POS + optional Windows/XPS dev path)
// ──────────────────────────────────────────────────────────────────────────────
// What it does
// • Single async pipeline to print Sale, Sale Return, Voucher, and Z-Report.
// • Resolves printer & store info from runtime settings (outlet/counter).
// • Resolves the correct ReceiptTemplate per doc type.
// • Normal path: Builds ESC/POS bytes and sends RAW via IRawPrinterService.
// • Dev path (toggleable): If printer is a virtual Windows printer (Microsoft PDF/XPS),
//   we render a monospace FlowDocument and print via XPS so you can save a PDF.
// • Cutter handled with a robust trailer.
//
// Toggle
// • Set VIRTUAL_PRINTER_DEV_MODE = true to enable Windows/XPS dev printing.
// • Set it to false (or comment) to force RAW ESC/POS always.
//
// Required DI services:
//   IRawPrinterService, IInvoiceSettingsLocalService, IInvoiceSettingsScopedService,
//   IIdentitySettingsService, ITerminalContext, IReceiptTemplateService.
// ──────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Models;            // CartLine (via using static below)
using Pos.Domain.Accounting;            // Voucher
using Pos.Domain.Entities;              // Sale, TillSession, ReceiptTemplate, ZReportModel, IdentitySettings
using Pos.Domain.Services;              // IRawPrinterService, IReceiptTemplateService, ITerminalContext, IIdentitySettingsService
using Pos.Domain.Settings;              // IInvoiceSettingsLocalService, IInvoiceSettingsScopedService
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine
using System.Threading;      // CancellationToken
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Printing
{
    public static class ReceiptPrinter
    {
        // ───────── Dev toggle: comment/flip for production ─────────
        private const bool VIRTUAL_PRINTER_DEV_MODE = false;

        // Fallbacks if DB is empty
        //public static string DefaultPrinterName = "POS80";
        //public static string DefaultStoreName = "My Store";

        // ---------------- DI helpers ----------------

        private static (IRawPrinterService raw,
                        IInvoiceSettingsLocalService invoiceLocal,
                        IInvoiceSettingsScopedService invoiceScoped,
                        IIdentitySettingsService identity,
                        ITerminalContext ctx,
                        IReceiptTemplateService tplSvc)
            ResolveServices()
        {
            var sp = App.Services;
            return (sp.GetRequiredService<IRawPrinterService>(),
                    sp.GetRequiredService<IInvoiceSettingsLocalService>(),
                    sp.GetRequiredService<IInvoiceSettingsScopedService>(),
                    sp.GetRequiredService<IIdentitySettingsService>(),
                    sp.GetRequiredService<ITerminalContext>(),
                    sp.GetRequiredService<IReceiptTemplateService>());
        }

        private static async Task<(string storeName, string printerName, string footer)>
            ResolveRuntimeAsync(CancellationToken ct = default)
        {
            var (raw, invoiceLocal, invoiceScoped, identity, ctx, _) = ResolveServices();

            var id = await identity.GetAsync(ctx.OutletId, ct);
            var loc = await invoiceLocal.GetForCounterWithFallbackAsync(ctx.CounterId, ct);
            var scop = await invoiceScoped.GetForOutletAsync(ctx.OutletId, ct);

            //string store = string.IsNullOrWhiteSpace(id?.OutletDisplayName) ? DefaultStoreName : id!.OutletDisplayName!;
            string store;
            if (!string.IsNullOrWhiteSpace(id?.OutletDisplayName))
            {
                store = id!.OutletDisplayName!;
            }
            else
            {
                // Mirror Receipt Builder behavior: fall back to outlet’s Name
                var outlets = App.Services.GetRequiredService<IOutletService>();
                
                store = !string.IsNullOrWhiteSpace(id?.OutletDisplayName) ? id!.OutletDisplayName : "MY STORE";
            }



            if (string.IsNullOrWhiteSpace(loc?.PrinterName))
                throw new InvalidOperationException("No receipt printer set for this counter. Open Backstage → Invoice Settings and pick a printer.");
            string printer = loc!.PrinterName!;

            string footer = string.IsNullOrWhiteSpace(scop?.FooterSale) ? "Thank you!" : scop!.FooterSale!;

            return (store, printer, footer);
        }

        //private static async Task<string> ResolveLatestReceiptPrinterAsync(CancellationToken ct)
        //{
        //    var sp = App.Services;
        //    var ctx = sp.GetRequiredService<ITerminalContext>();
        //    var inv = sp.GetRequiredService<IInvoiceSettingsLocalService>();

        //    var latest = await inv.GetForCounterWithFallbackAsync(ctx.CounterId, ct);
        //    var name = latest?.PrinterName;

        //    if (string.IsNullOrWhiteSpace(name))
        //        throw new InvalidOperationException("No receipt printer selected in Invoice Settings for this counter.");

        //    return name!;
        //}


        private static async Task<ReceiptTemplate> ResolveTemplateAsync(ReceiptDocType type, CancellationToken ct = default)
        {
            var (_, _, _, _, ctx, tplSvc) = ResolveServices();
            return await tplSvc.GetAsync(ctx.OutletId, type, ct);
        }

        // ---------------- Single source of truth (async) ----------------

        /// <summary>
        /// Generic print for all receipt types. If VIRTUAL_PRINTER_DEV_MODE is enabled and the target
        /// queue is a Windows virtual printer (Microsoft Print to PDF / XPS / OneNote), Sale/SaleReturn
        /// will render via Windows/XPS. Otherwise, we send RAW ESC/POS bytes to the printer.
        /// </summary>
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
            string? salesmanName = null,
            CancellationToken ct = default)
        {
            // Resolve store/printer/footer and identity
            var (storeResolved, printerResolved, footerResolved) = await ResolveRuntimeAsync(ct);
            var storeName = storeNameOverride ?? storeResolved;

            var (_, _, _, identitySvc, ctx, _) = ResolveServices();
            var id = await identitySvc.GetAsync(ctx.OutletId, ct);

            // Optional address & phone (pass to your content builders if needed)
            string? address = null;
            {
                var parts = new[] { id?.AddressLine1, id?.AddressLine2 }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .ToArray();
                if (parts.Length > 0)
                    address = string.Join("\n", parts);
            }
            string? phone = string.IsNullOrWhiteSpace(id?.Phone) ? null : id!.Phone!.Trim();

            // ---------- DEV path: Windows/XPS for Sale & SaleReturn only ----------
            if (VIRTUAL_PRINTER_DEV_MODE &&
                WindowsReceiptPrintService.IsVirtualWindowsPrinter(printerResolved) &&
                (docType == ReceiptDocType.Sale || docType == ReceiptDocType.SaleReturn))
            {
                // Build plain text using your sale builder so PDF matches preview
                var baseText = ReceiptContentBuilder.BuildSaleText(
                    tpl,
                    id ?? new IdentitySettings(),
                    cart ?? new List<CartLine>(),
                    sale!,
                    cashierName ?? "Cashier",
                    salesmanName
                );

                if (docType == ReceiptDocType.SaleReturn)
                    baseText = baseText.Replace("SALE INVOICE", "SALE RETURN");

                var linesForWindows = baseText
                    .Replace("\r", "")
                    .Split('\n')
                    .ToList();

                await WindowsReceiptPrintService.PrintAsync(printerResolved, tpl, linesForWindows, ct);
                return; // Do not continue to RAW path
            }

            // ---------- Normal ESC/POS path (all types) ----------
        

            // ESC/POS compose: init + optional pre-logo margin + logo + reset + (bold on?) + text + (bold off) + trailer
            var init = new byte[] { 0x1B, 0x40 }; // ESC @

            var preLogoMargin = tpl.TopMarginLines > 0
                ? new byte[] { 0x1B, 0x64, (byte)Math.Min(255, Math.Max(0, tpl.TopMarginLines)) } // ESC d n
                : Array.Empty<byte>();

            // Raster logo aligned and width-capped
            byte[] logoBytes = Array.Empty<byte>();
            if (tpl.ShowLogoOnReceipt && id?.LogoPng is { Length: > 0 })
            {
                int paperMax = EscPosImageEncoder.GetMaxDotsForPaperWidthMm(tpl.PaperWidthMm);
                int cap = (tpl.LogoMaxWidthPx > 0)
                            ? tpl.LogoMaxWidthPx
                            : (tpl.PaperWidthMm <= 58 ? 280 : 460);
                int target = Math.Min(paperMax, cap);

                logoBytes = EscPosImageEncoder.EncodeLogoPngAligned(
                    id.LogoPng!, target, paperMax, tpl.LogoAlignment);
            }

            // ---------- Determine plain text and detected columns (Sale / SaleReturn) ----------
            string? plain = null;
            int detectedCols = 0;

            if (docType == ReceiptDocType.Sale || docType == ReceiptDocType.SaleReturn)
            {
                plain = ReceiptContentBuilder.BuildSaleText(
                    tpl,
                    id ?? new IdentitySettings(),
                    cart ?? new List<CartLine>(),
                    sale!,
                    cashierName ?? "Cashier",
                    salesmanName
                );
                if (docType == ReceiptDocType.SaleReturn)
                    plain = plain.Replace("SALE INVOICE", "SALE RETURN");

                // Detect body width (columns) from the text we’ll actually print
                static int DetectCols(string s)
                {
                    var lines = s.Replace("\r", "").Split('\n');
                    int best = 0;
                    foreach (var raw in lines)
                    {
                        var l = raw.TrimEnd();                 // ignore trailing spaces
                        if (l.Length == 0) continue;
                        // treat dashed/equals lines as definitive width if long
                        if (l.All(ch => ch == '-' || ch == '=')) best = Math.Max(best, l.Length);
                        else best = Math.Max(best, l.Length);
                    }
                    // clamp to plausible Font-A widths
                    return Math.Max(10, Math.Min(best, 60));
                }

                detectedCols = DetectCols(plain);
            }

            // ---------- Compute head width and center the body window ----------
            int headDots = (tpl.PaperWidthMm >= 80) ? 576 : 384;   // 80mm heads are almost always 576
            int charDots = 12;                                     // Font-A approx. width in dots

            int contentDots =
                (docType == ReceiptDocType.Sale || docType == ReceiptDocType.SaleReturn)
                    ? Math.Min(headDots, Math.Max(charDots * 10, detectedCols * charDots))
                    : headDots; // other docs: let it span full head

            int leftOffsetDots = (headDots - contentDots) / 2;     // symmetric margins L = R
            ushort areaWidthDots = (ushort)contentDots;

            byte nL = (byte)(leftOffsetDots & 0xFF), nH = (byte)(leftOffsetDots >> 8);
            byte wL = (byte)(areaWidthDots & 0xFF), wH = (byte)(areaWidthDots >> 8);

            // ---------- Reset after logo, normalize geometry, then left-align the body ----------
            byte[] resetTextMode = new byte[]
            {
    0x1B, 0x40,            // ESC @ : hard reset (ensures raster/logo state cleared)
    0x1B, (byte)'M', 0x00, // Font A
    0x1B, (byte)'!', 0x00, // normal
    0x1B, (byte)'E', 0x00, // bold off
    0x1B, 0x20, 0x00,      // char spacing = 0
    0x1B, (byte)'a', 0x00, // left align
    0x1D, 0x4C, nL, nH,    // GS L : left margin (center window)
    0x1D, 0x57, wL, wH     // GS W : printable width = detected body width
            };

            // ---------- Build bytes ----------
            // 2) build body bytes
            byte[] textBytes = docType switch
            {
                ReceiptDocType.Sale => EscPosTextEncoder.FromPlainText(plain!),
                ReceiptDocType.SaleReturn => EscPosTextEncoder.FromPlainText(plain!),
                ReceiptDocType.Voucher => VoucherReceiptBuilder.Build(voucher!, tpl),
                ReceiptDocType.ZReport => ZReportReceiptBuilder.Build(z!, tpl),
                _ => throw new NotSupportedException(docType.ToString())
            };

            // 3) strip leading ESC @ from body so our GS L/GS W stay effective
            if (textBytes.Length >= 2 && textBytes[0] == 0x1B && textBytes[1] == 0x40)
                textBytes = textBytes[2..];
                  
            byte[] boldOn = (tpl.MakeAllTextBold) ? new byte[] { 0x1B, (byte)'E', 0x01 } : Array.Empty<byte>();
            byte[] boldOff = (tpl.MakeAllTextBold) ? new byte[] { 0x1B, (byte)'E', 0x00 } : Array.Empty<byte>();

            // Trailer: feed a few lines + cut
            var trailer = new byte[]
            {
                0x1B, 0x64, 0x03,       // ESC d n : feed 3 lines
                0x1D, 0x56, 0x41, 0x10  // GS V A n : partial cut (n=16 is a safe feed-and-cut)
            };

            // Compose final payload
            var payload = Combine(
                Combine(init, preLogoMargin),
                Combine(logoBytes,
                    Combine(resetTextMode,
                        Combine(boldOn, Combine(textBytes, Combine(boldOff, trailer))))
                )
            );

            var (raw, _, _, _, _, _) = ResolveServices();
            await raw.SendEscPosAsync(printerResolved, payload, ct);

            // local helper
            static byte[] Combine(byte[] a, byte[] b)
            {
                if (a.Length == 0) return b;
                if (b.Length == 0) return a;
                var r = new byte[a.Length + b.Length];
                Buffer.BlockCopy(a, 0, r, 0, a.Length);
                Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
                return r;
            }
        }

        // ---------------- Typed convenience wrappers (async) ----------------

        public static async Task PrintSaleAsync(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string cashierName,
            string? salesmanName,
            CancellationToken ct = default)
        {
            var tpl = await ResolveTemplateAsync(ReceiptDocType.Sale, ct);
            await PrintAsync(
                ReceiptDocType.Sale, tpl,
                sale: sale,
                cart: cart?.ToList(),
                till: till,
                cashierName: cashierName,
                salesmanName: salesmanName,
                ct: ct);
        }

        public static async Task PrintSaleReturnAsync(
            Sale sale,
            IEnumerable<CartLine> cart,
            TillSession? till,
            string cashierName,
            string? salesmanName,
            CancellationToken ct = default)
        {
            var tpl = await ResolveTemplateAsync(ReceiptDocType.SaleReturn, ct);
            await PrintAsync(
                ReceiptDocType.SaleReturn, tpl,
                sale: sale,
                cart: cart?.ToList(),
                till: till,
                cashierName: cashierName,
                salesmanName: salesmanName,
                ct: ct);
        }

        public static async Task PrintVoucherAsync(
            Voucher voucher,
            CancellationToken ct = default)
        {
            var tpl = await ResolveTemplateAsync(ReceiptDocType.Voucher, ct);
            await PrintAsync(ReceiptDocType.Voucher, tpl, voucher: voucher, ct: ct);
        }

        public static async Task PrintZReportAsync(
            ZReportModel z,
            CancellationToken ct = default)
        {
            var tpl = await ResolveTemplateAsync(ReceiptDocType.ZReport, ct);
            await PrintAsync(ReceiptDocType.ZReport, tpl, z: z, ct: ct);
        }

        // ---------------- Optional public trailer util ----------------

        /// <summary>
        /// Minimal ESC/POS trailer: init, feed, and a single cut command.
        /// Use partialCut=true for partial; otherwise full cut.
        /// </summary>
        public static byte[] AppendEscPosTrailer(
            byte[] data,
            int feedLines = 4,
            bool cut = true,
            bool partialCut = false,
            bool kickDrawer = false)
        {
            var trailer = new List<byte>(24);

            // Init
            trailer.AddRange(new byte[] { 0x1B, 0x40 });               // ESC @

            // Feed n lines (ESC d n)
            trailer.AddRange(new byte[] { 0x1B, 0x64, (byte)Math.Max(0, feedLines) });

            // Optional drawer
            if (kickDrawer)
                trailer.AddRange(new byte[] { 0x1B, 0x70, 0x00, 0x32, 0xC8 });

            if (cut)
            {
                // Preferred: GS V 66 n (feed-and-cut). n=0 is fine for most printers.
                // Use GS V 65 n for partial.
                if (partialCut)
                    trailer.AddRange(new byte[] { 0x1D, 0x56, 0x41, 0x00 }); // partial feed-and-cut
                else
                    trailer.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 }); // full feed-and-cut
            }

            // Final LF helps some USB adapters flush
            trailer.Add(0x0A);

            return data.Concat(trailer).ToArray();
        }
    }
}
