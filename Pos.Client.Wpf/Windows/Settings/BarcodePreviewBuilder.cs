using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace Pos.Client.Wpf.Windows.Settings
{
    public static class BarcodePreviewBuilder
    {
        // mm → px
        private static double PxPerMm(int dpi) => dpi / 25.4;

        /// <summary>
        /// Renders a label preview with an absolutely-positioned barcode block and free-positioned texts.
        /// All coordinates for texts are in millimeters from the label's top-left.
        /// The barcode block is defined by margins (mm) and an optional fixed height (mm).
        /// </summary>
        public static ImageSource Build(
            // geometry (mm / dpi)
            int labelWidthMm, int labelHeightMm,
            int marginLeftMm, int marginTopMm, // kept for compatibility (not used for barcode layout anymore)
            int dpi,
            // barcode
            string codeType, string payload,
            // text (3 fields)
            bool showName, bool showPrice, bool showSku,
            string nameText, string priceText, string skuText,
            int fontSizePt,
            double nameXmm, double nameYmm,
            double priceXmm, double priceYmm,
            double skuXmm, double skuYmm,
            // NEW: barcode block controls (mm)
            double barcodeMarginLeftMm, double barcodeMarginTopMm,
            double barcodeMarginRightMm, double barcodeMarginBottomMm,
            double barcodeHeightMm,
            // NEW: business name (content + visibility + position in mm)
            bool showBusinessName, string businessName, double businessXmm, double businessYmm
        )
        {
            // ---- Units
            // WPF draws in DIPs (96 DIPs = 1 inch). Printers/ZXing use device pixels.
            double dipPerMm = 96.0 / 25.4;   // convert mm → DIP
            double pxPerDip = dpi / 96.0;    // convert DIP → px
            double pxPerMm = dpi / 25.4;    // convert mm → px (for ZXing only)

            // ---- Label size (DIP)
            double widthDip = Math.Max(32, labelWidthMm * dipPerMm);
            double heightDip = Math.Max(32, labelHeightMm * dipPerMm);

            // ---- Compute barcode rect from margins/height (all in DIP)
            // Convert mm → DIP
            double leftDip = barcodeMarginLeftMm * dipPerMm;
            double topDip = barcodeMarginTopMm * dipPerMm;
            double rightDip = barcodeMarginRightMm * dipPerMm;
            double bottomDip = barcodeMarginBottomMm * dipPerMm;

            // Width is always from left/right margins
            double bcWidthDip = Math.Max(2, widthDip - leftDip - rightDip);

            // Height: if explicit height is given (>0), use that; else derive from top/bottom margins
            double bcHeightDip = (barcodeHeightMm > 0.0)
                ? Math.Min(barcodeHeightMm * dipPerMm, Math.Max(2, heightDip - topDip - bottomDip))
                : Math.Max(2, heightDip - topDip - bottomDip);

            // Final barcode rect (clamped to label)
            Rect bcRect = new Rect(
                x: Math.Clamp(leftDip, 0, Math.Max(0, widthDip - 2)),
                y: Math.Clamp(topDip, 0, Math.Max(0, heightDip - 2)),
                width: Math.Max(2, Math.Min(bcWidthDip, widthDip - leftDip)),
                height: Math.Max(2, Math.Min(bcHeightDip, heightDip - topDip))
            );

            // ---- ZXing bitmap size (px) — match barcode rect size
            int zxW = Math.Max(8, (int)Math.Round(bcRect.Width * pxPerDip));
            int zxH = Math.Max(8, (int)Math.Round(bcRect.Height * pxPerDip));

            // ---- RenderTarget size (px)
            int bmpW = Math.Max(32, (int)Math.Round(widthDip * pxPerDip));
            int bmpH = Math.Max(32, (int)Math.Round(heightDip * pxPerDip));

            var dv = new DrawingVisual();
            using var dc = dv.RenderOpen();

            // Label background + hairline border (DIP)
            var labelRect = new Rect(0, 0, widthDip, heightDip);
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), labelRect);

            // Format + payload normalization
            var (format, encoded) = CoerceFormatAndPayload(codeType, payload);

            // ---- Barcode image (pixel) → draw into DIP rect
            try
            {
                var writer = new ZXing.BarcodeWriterPixelData
                {
                    Format = format,
                    Options = new ZXing.Common.EncodingOptions
                    {
                        Width = zxW,
                        Height = zxH,
                        Margin = 0,
                        PureBarcode = true
                    }
                };
                var pix = writer.Write(encoded);

                var bmp = BitmapSource.Create(
                    pix.Width, pix.Height,      // px
                    dpi, dpi,                   // dpi
                    PixelFormats.Bgra32, null,
                    pix.Pixels, pix.Width * 4);

                dc.DrawImage(bmp, bcRect);
            }
            catch
            {
                // draw a visible error block if ZXing fails
                var pen = new Pen(Brushes.IndianRed, 2);
                dc.DrawRectangle(Brushes.White, pen, bcRect);
                dc.DrawLine(pen, bcRect.TopLeft, bcRect.BottomRight);
                dc.DrawLine(pen, bcRect.BottomLeft, bcRect.TopRight);
            }

            // ---- Texts at absolute mm positions (independent of barcode)
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            void DrawAtMm(string text, bool visible, double xmm, double ymm)
            {
                if (!visible || string.IsNullOrWhiteSpace(text)) return;

                double xDip = xmm * dipPerMm;
                double yDip = ymm * dipPerMm;

                // optional: clamp to label bounds (soft clamp: skip if outside)
                if (xDip > widthDip - 2 || yDip > heightDip - 2) return;

                // max width = to right edge minus 2 dips padding
                double maxW = Math.Max(0, widthDip - xDip - 2);
                DrawText(dc, text, typeface, fontSizePt, Brushes.Black, xDip, yDip, maxW);
            }

            // existing 3 fields
            DrawAtMm(nameText, showName, nameXmm, nameYmm);
            DrawAtMm(priceText, showPrice, priceXmm, priceYmm);
            DrawAtMm(skuText, showSku, skuXmm, skuYmm);

            // NEW: business name
            DrawAtMm(businessName ?? "", showBusinessName, businessXmm, businessYmm);

            dc.Close();

            var rtb = new RenderTargetBitmap(bmpW, bmpH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static void DrawText(
            DrawingContext dc, string text, Typeface tf, double fontPt,
            Brush brush, double x, double y, double maxWidthDip)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // WPF fonts are specified in DIP; convert pt → DIP (1 pt = 96/72 DIP)
            double fontDip = fontPt * (96.0 / 72.0);

            // pixelsPerDip should come from the current display; 1.0 is OK
            double pixelsPerDip = 1.0;
            if (Application.Current?.MainWindow != null)
                pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;

            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                tf,
                fontDip,
                brush,
                pixelsPerDip)
            {
                MaxTextWidth = Math.Max(0, maxWidthDip),
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Left
            };

            dc.DrawText(ft, new Point(x, y));
        }

        private static (BarcodeFormat format, string payload) CoerceFormatAndPayload(string codeType, string raw)
        {
            string digitsOnly = new(raw?.Where(char.IsDigit).ToArray());
            switch ((codeType ?? "Code128").ToUpperInvariant())
            {
                case "EAN13":
                case "EAN-13":
                    var ean = digitsOnly.PadLeft(12, '0');
                    if (ean.Length > 12) ean = ean[^12..];
                    return (ZXing.BarcodeFormat.EAN_13, ean);

                case "UPCA":
                case "UPC-A":
                    var upc = digitsOnly.PadLeft(11, '0');
                    if (upc.Length > 11) upc = upc[^11..];
                    return (ZXing.BarcodeFormat.UPC_A, upc);

                default:
                    return (ZXing.BarcodeFormat.CODE_128, string.IsNullOrWhiteSpace(raw) ? "123456789012" : raw);
            }
        }
    }
}
