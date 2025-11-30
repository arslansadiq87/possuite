// Pos.Client.Wpf/Printing/LabelPrintService.cs
using System;
using System.Linq;
using System.Printing;                // ReachFramework
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;        // FixedDocument, FixedPage, PageContent
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
                throw new InvalidOperationException("No label printer selected. Please select a label printer in Label Settings or Invoice Settings.");

            using var server = new LocalPrintServer();
            var queues = server.GetPrintQueues();

            var q = queues.FirstOrDefault(p =>
                string.Equals(p.FullName, printerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase))
                ?? queues.FirstOrDefault(p =>
                    p.FullName.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Name.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (q != null) return q;

            var available = string.Join(", ", queues.Select(p => p.FullName));
            throw new InvalidOperationException(
                $"Label printer \"{printerName}\" not found. Available: {available}");
        }

        // --- NEW: shared grid builder (snaps page to device dots to avoid scaling/clipping) ---
        private static FixedDocument BuildTiledDocument(
    BarcodeLabelSettings s,
    Func<int, ImageSource> buildImage)
        {
            int columns = Math.Max(1, s.Columns);
            int rows = Math.Max(1, s.Rows);
            int dpi = Math.Max(96, s.Dpi <= 0 ? 203 : s.Dpi);

            // Page mm = labels + gaps
            double pageWidthMm = columns * s.LabelWidthMm + (columns - 1) * s.HorizontalGapMm;
            double pageHeightMm = rows * s.LabelHeightMm + (rows - 1) * s.VerticalGapMm;

            // Snap to device pixels: pagePx = round(mm * dpi / 25.4)
            int pageWPx = (int)Math.Round(pageWidthMm * dpi / 25.4);
            int pageHPx = (int)Math.Round(pageHeightMm * dpi / 25.4);

            // DIPs for WPF page (96 dpi)
            double pageWdip = pageWPx * (96.0 / dpi);
            double pageHdip = pageHPx * (96.0 / dpi);

            // Per-label DIP sizes (mm -> DIP; relative layout correct)
            double mmToDip = 96.0 / 25.4;
            double labelW = s.LabelWidthMm * mmToDip;
            double labelH = s.LabelHeightMm * mmToDip;
            double gapW = s.HorizontalGapMm * mmToDip;
            double gapH = s.VerticalGapMm * mmToDip;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWdip, pageHdip);

            var page = new FixedPage
            {
                Width = pageWdip,
                Height = pageHdip,
                Background = Brushes.White
            };

            // --- Anti-blank anchor (prevents first-label skip on some thermal drivers) ---
            var anchorBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            anchorBrush.Freeze();
            var anchor = new Border
            {
                Width = pageWdip,
                Height = 0.6,                 // ~1 scanline at common DPIs
                Background = anchorBrush,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            FixedPage.SetLeft(anchor, 0);
            FixedPage.SetTop(anchor, 0);
            page.Children.Add(anchor);

            // Tile labels
            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    var src = buildImage(idx++);
                    var img = new Image
                    {
                        Source = src,
                        Width = labelW,
                        Height = labelH,
                        Stretch = Stretch.None,
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = true
                    };

                    double x = c * (labelW + gapW);
                    double y = r * (labelH + gapH);
                    FixedPage.SetLeft(img, x);
                    FixedPage.SetTop(img, y);
                    page.Children.Add(img);
                }
            }

            // Force layout
            page.Measure(new Size(pageWdip, pageHdip));
            page.Arrange(new Rect(0, 0, pageWdip, pageHdip));
            page.UpdateLayout();

            var pc = new PageContent();
            ((IAddChild)pc).AddChild(page);
            doc.Pages.Add(pc);
            return doc;
        }

        // Simple version: print from settings (no live sample fields)
        public Task PrintSampleAsync(BarcodeLabelSettings s, CancellationToken ct = default)
        {
            double zoom = s.BarcodeZoomPct.HasValue
                ? Math.Clamp(s.BarcodeZoomPct.Value / 100.0, 0.3, 2.0)
                : 1.0;

            var doc = BuildTiledDocument(
                s,
                _ => BarcodePreviewBuilder.Build(
                        labelWidthMm: s.LabelWidthMm,
                        labelHeightMm: s.LabelHeightMm,
                        marginLeftMm: s.MarginLeftMm,
                        marginTopMm: s.MarginTopMm,
                        dpi: Math.Max(96, s.Dpi <= 0 ? 203 : s.Dpi),
                        codeType: s.CodeType,
                        payload: "123456789012", // generic
                        showName: s.ShowName,
                        showPrice: s.ShowPrice,
                        showSku: s.ShowSku,
                        nameText: "Sample Item",
                        priceText: "0.00",
                        skuText: "SKU-001",
                        fontSizePt: s.FontSizePt,
                        nameXmm: s.NameXmm,
                        nameYmm: s.NameYmm,
                        priceXmm: s.PriceXmm,
                        priceYmm: s.PriceYmm,
                        skuXmm: s.SkuXmm,
                        skuYmm: s.SkuYmm,
                        barcodeMarginLeftMm: s.BarcodeMarginLeftMm,
                        barcodeMarginTopMm: s.BarcodeMarginTopMm,
                        barcodeMarginRightMm: s.BarcodeMarginRightMm,
                        barcodeMarginBottomMm: s.BarcodeMarginBottomMm,
                        barcodeHeightMm: s.BarcodeHeightMm,
                        showBusinessName: s.ShowBusinessName,
                        businessName: s.BusinessName ?? string.Empty,
                        businessXmm: s.BusinessXmm,
                        businessYmm: s.BusinessYmm,
                        nameAlign: "Left",
                        priceAlign: "Left",
                        skuAlign: "Left",
                        businessAlign: "Left",
                        barcodeZoom: zoom
                )
            );

            var queue = ResolveQueueOrThrow(s.PrinterName ?? "");
            var pd = new PrintDialog { PrintQueue = queue };

            // lock page size into the ticket (Unknown + exact WPF DIPs)
            var pageSize = doc.DocumentPaginator.PageSize;
            var baseTicket = queue.DefaultPrintTicket ?? new PrintTicket();
            var ticket = new PrintTicket
            {
                PageMediaSize = new PageMediaSize(PageMediaSizeName.Unknown, pageSize.Width, pageSize.Height)
            };
            var result = queue.MergeAndValidatePrintTicket(baseTicket, ticket);
            pd.PrintTicket = result.ValidatedPrintTicket ?? ticket;

            pd.PrintDocument(doc.DocumentPaginator, "POS Label – Sample");
            return Task.CompletedTask;
        }

        // Extended: print exactly what the preview shows (live sample fields)
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

            var doc = BuildTiledDocument(
                s,
                _ => BarcodePreviewBuilder.Build(
                        labelWidthMm: s.LabelWidthMm,
                        labelHeightMm: s.LabelHeightMm,
                        marginLeftMm: s.MarginLeftMm,
                        marginTopMm: s.MarginTopMm,
                        dpi: Math.Max(96, s.Dpi <= 0 ? 203 : s.Dpi),
                        codeType: s.CodeType,
                        payload: string.IsNullOrWhiteSpace(sampleCode) ? sampleSku : sampleCode,
                        showName: s.ShowName,
                        showPrice: s.ShowPrice,
                        showSku: s.ShowSku,
                        nameText: sampleName,
                        priceText: samplePrice,
                        skuText: sampleSku,
                        fontSizePt: s.FontSizePt,
                        nameXmm: s.NameXmm,
                        nameYmm: s.NameYmm,
                        priceXmm: s.PriceXmm,
                        priceYmm: s.PriceYmm,
                        skuXmm: s.SkuXmm,
                        skuYmm: s.SkuYmm,
                        barcodeMarginLeftMm: s.BarcodeMarginLeftMm,
                        barcodeMarginTopMm: s.BarcodeMarginTopMm,
                        barcodeMarginRightMm: s.BarcodeMarginRightMm,
                        barcodeMarginBottomMm: s.BarcodeMarginBottomMm,
                        barcodeHeightMm: s.BarcodeHeightMm,
                        showBusinessName: showBusinessName,
                        businessName: businessName ?? string.Empty,
                        businessXmm: s.BusinessXmm,
                        businessYmm: s.BusinessYmm,
                        nameAlign: "Left",
                        priceAlign: "Left",
                        skuAlign: "Left",
                        businessAlign: "Left",
                        barcodeZoom: zoom
                )
            );

            var queue = ResolveQueueOrThrow(s.PrinterName ?? "");
            var pd = new PrintDialog { PrintQueue = queue };

            // lock page size into the ticket (Unknown + exact WPF DIPs)
            var pageSize = doc.DocumentPaginator.PageSize;
            var baseTicket = queue.DefaultPrintTicket ?? new PrintTicket();
            var ticket = new PrintTicket
            {
                PageMediaSize = new PageMediaSize(PageMediaSizeName.Unknown, pageSize.Width, pageSize.Height)
            };
            var result = queue.MergeAndValidatePrintTicket(baseTicket, ticket);
            pd.PrintTicket = result.ValidatedPrintTicket ?? ticket;

            pd.PrintDocument(doc.DocumentPaginator, "POS Label – Sample");
            return Task.CompletedTask;
        }
    }
}
