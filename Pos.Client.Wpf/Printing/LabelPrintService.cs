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
        // 1) Simple version (interface #1)
        public async Task PrintSampleAsync(BarcodeLabelSettings s, CancellationToken ct = default)
        {
            await PrintSampleAsync(
                s,
                sampleCode: s.CodeType?.ToUpperInvariant() switch
                {
                    "EAN8" or "EAN-8" => "5512345",        // 7 digits -> preview adds check
                    "EAN13" or "EAN-13" => "590123412345",   // 12 digits -> preview adds check
                    "UPCA" or "UPC-A" => "04210000526",    // 11 digits -> preview adds check
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
            // Build preview image (exactly what we will print)
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
                // Alignments – use defaults for now (no persistence required)
                nameAlign: "Left",
                priceAlign: "Left",
                skuAlign: "Left",
                businessAlign: "Left"
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

            // Print dialog & queue
            var pd = new PrintDialog();
            if (!string.IsNullOrWhiteSpace(s.PrinterName))
            {
                try { pd.PrintQueue = new LocalPrintServer().GetPrintQueue(s.PrinterName); }
                catch { /* fallback to default printer */ }
            }

            // Ticket close to device media
            var baseTicket = pd.PrintQueue?.UserPrintTicket ?? pd.PrintTicket ?? new PrintTicket();
            var ticket = new PrintTicket
            {
                CopyCount = 1,
                PageOrientation = PageOrientation.Portrait
            };

            // Try choose exact media size (in DIPs)
            var caps = pd.PrintQueue?.GetPrintCapabilities();
            PageMediaSize? chosen = null;
            if (caps?.PageMediaSizeCapability != null)
            {
                const double tolDip = 2.0; // ~0.5 mm
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

            var result = pd.PrintQueue?.MergeAndValidatePrintTicket(baseTicket, ticket);
            pd.PrintTicket = result?.ValidatedPrintTicket ?? ticket;

            pd.PrintDocument(doc.DocumentPaginator, "POS Label – Sample");
            return Task.CompletedTask;
        }
    }
}
