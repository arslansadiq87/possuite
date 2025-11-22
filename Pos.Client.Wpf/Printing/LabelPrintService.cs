using System;
using System.Printing;                // ReachFramework
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;        // FixedPage, PageContent
using System.Windows.Markup;           // IAddChild
using System.Windows.Media;
using Pos.Client.Wpf.Windows.Settings; // BarcodePreviewBuilder
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Printing
{
    public sealed class LabelPrintService : ILabelPrintService
    {
        private static PrintQueue ResolveQueueOrThrow(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new InvalidOperationException("No label printer selected. Please select a label printer in Barcode Label Settings.");

            // Try exact match among local + connected printers
            var lps = new LocalPrintServer(); // “local” also enumerates user’s connected queues
            var queues = lps.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            });

            // exact (case-insensitive)
            var q = queues.FirstOrDefault(p =>
                string.Equals(p.FullName, printerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));

            if (q != null) return q;

            // relaxed contains (helps when UI shows trimmed or friendly names)
            q = queues.FirstOrDefault(p =>
                p.FullName.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Name.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (q != null) return q;

            var available = string.Join(", ", queues.Select(p => p.FullName));
            throw new InvalidOperationException(
                $"Label printer \"{printerName}\" not found. Available: {available}");
        }
        // 1) Simple version (interface #1)
        public async Task PrintSampleAsync(BarcodeLabelSettings s, CancellationToken ct = default)
        {
            await PrintSampleAsync(
                s,
                sampleCode: s.CodeType?.ToUpperInvariant() switch
                {
                    "EAN8" or "EAN-8" => "5512345",
                    "EAN13" or "EAN-13" => "590123412345",
                    "UPCA" or "UPC-A" => "04210000526",
                    _ => "123456789012"
                },
                sampleName: "Sample Item",
                samplePrice: "Rs 999",
                sampleSku: "SKU-001",
                showBusinessName: s.ShowBusinessName,
                businessName: string.IsNullOrWhiteSpace(s.BusinessName) ? "My Business" : s.BusinessName,
                ct: ct);
        }

        // 2) Extended version (interface #2)
        public Task PrintSampleAsync(
            BarcodeLabelSettings s,
            string sampleCode,
            string sampleName,
            string samplePrice,
            string sampleSku,
            bool showBusinessName,
            string businessName,
            CancellationToken ct = default)
        {
            double zoom = s.BarcodeZoomPct.HasValue
    ? Math.Clamp(s.BarcodeZoomPct.Value / 100.0, 0.3, 2.0)
    : 1.0;
            var img = BarcodePreviewBuilder.Build(
                labelWidthMm: s.LabelWidthMm,
                labelHeightMm: s.LabelHeightMm,
                marginLeftMm: s.MarginLeftMm,
                marginTopMm: s.MarginTopMm,
                dpi: s.Dpi,
                codeType: s.CodeType,
                payload: sampleCode,
                showName: s.ShowName,
                showPrice: s.ShowPrice,
                showSku: s.ShowSku,
                nameText: sampleName,
                priceText: samplePrice,
                skuText: sampleSku,
                fontSizePt: s.FontSizePt,
                nameXmm: s.NameXmm, nameYmm: s.NameYmm,
                priceXmm: s.PriceXmm, priceYmm: s.PriceYmm,
                skuXmm: s.SkuXmm, skuYmm: s.SkuYmm,
                barcodeMarginLeftMm: s.BarcodeMarginLeftMm,
                barcodeMarginTopMm: s.BarcodeMarginTopMm,
                barcodeMarginRightMm: s.BarcodeMarginRightMm,
                barcodeMarginBottomMm: s.BarcodeMarginBottomMm,
                barcodeHeightMm: s.BarcodeHeightMm,
                showBusinessName: showBusinessName,
                businessName: businessName,
                businessXmm: s.BusinessXmm,
                businessYmm: s.BusinessYmm,
                nameAlign: "Left",
                priceAlign: "Left",
                skuAlign: "Left",
                businessAlign: "Left",
                barcodeZoom: zoom
            );


            // Snap page size to device dots to avoid driver rounding
            int wPx = (int)Math.Round(s.LabelWidthMm * s.Dpi / 25.4);
            int hPx = (int)Math.Round(s.LabelHeightMm * s.Dpi / 25.4);
            double pageWdip = wPx * (96.0 / s.Dpi);
            double pageHdip = hPx * (96.0 / s.Dpi);

            // FixedPage sized to exact DIPs (no scaling)
            var page = new FixedPage { Width = pageWdip, Height = pageHdip, Background = Brushes.White };
            var imgCtrl = new Image
            {
                Source = img,
                Width = pageWdip,
                Height = pageHdip,
                Stretch = Stretch.None,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            FixedPage.SetLeft(imgCtrl, 0);
            FixedPage.SetTop(imgCtrl, 0);
            page.Children.Add(imgCtrl);
            page.Measure(new Size(pageWdip, pageHdip));
            page.Arrange(new Rect(0, 0, pageWdip, pageHdip));
            page.UpdateLayout();

            var pc = new PageContent();
            ((IAddChild)pc).AddChild(page);
            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWdip, pageHdip);
            doc.Pages.Add(pc);



            // ---------- CHANGED: resolve the exact queue or throw ----------
            var queue = ResolveQueueOrThrow(s.PrinterName);

            var pd = new PrintDialog
            {
                PrintQueue = queue
            };
            // Build PrintTicket against THIS queue (no fallback/defaults)
            var baseTicket = queue.UserPrintTicket ?? pd.PrintTicket ?? new PrintTicket();
            var ticket = new PrintTicket
            {
                CopyCount = 1,
                PageOrientation = PageOrientation.Portrait
            };

            var caps = queue.GetPrintCapabilities();
            PageMediaSize? chosen = null;
            if (caps?.PageMediaSizeCapability != null)
            {
                const double tolDip = 2.0;
                foreach (var ms in caps.PageMediaSizeCapability)
                {
                    if (ms.Width.HasValue && ms.Height.HasValue &&
                        Math.Abs(ms.Width.Value - pageWdip) <= tolDip &&
                        Math.Abs(ms.Height.Value - pageHdip) <= tolDip)
                    {
                        chosen = ms; break;
                    }
                }
            }

            ticket.PageMediaSize = chosen ?? new PageMediaSize(PageMediaSizeName.Unknown, pageWdip, pageHdip);
            if (caps?.PageMediaTypeCapability?.Contains(PageMediaType.Label) == true)
                ticket.PageMediaType = PageMediaType.Label;

            // AFTER
            var result = queue.MergeAndValidatePrintTicket(baseTicket, ticket);
            pd.PrintTicket = result.ValidatedPrintTicket ?? ticket;

            // Print directly (no dialog UI)
            pd.PrintDocument(doc.DocumentPaginator, "POS Label – Sample");
            return Task.CompletedTask;
        }
    }
}
