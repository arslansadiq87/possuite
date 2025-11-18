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
        /// 
        private static int GetPixelsPerModule(int dpi)
        {
            // 203dpi => 3px (~0.375 mm), 300dpi => 4px (~0.339 mm)
            if (dpi <= 0) return 3;
            return dpi switch
            {
                203 => 3,
                300 => 4,
                _ => Math.Max(3, (int)Math.Round(dpi / 25.4 / 3.03)) // ~0.33 mm -> dpi/76.77
            };
        }

        // Returns (barModules, quietLeftModules, quietRightModules)
        private static (int bars, int ql, int qr) GetSymbologyModuleSpec(string codeType, string payload, out int code128SymbolChars)
        {
            code128SymbolChars = 0;
            switch ((codeType ?? "").ToUpperInvariant())
            {
                case "EAN13":
                case "EAN-13":
                    // 95 modules bars; quiet 11X each side
                    return (95, 11, 11);

                case "UPCA":
                case "UPC-A":
                    // 95 modules bars; quiet 9X each side
                    return (95, 9, 9);

                case "EAN8":
                case "EAN-8":
                    // 67 modules bars; quiet 7X each side
                    return (67, 7, 7);

                case "CODE128":
                case "CODE 128":
                    // Code128 width depends on symbol characters.
                    // Each symbol char consumes 11 modules. Start=11, checksum=11, stop+term=15.
                    // For a decent estimate that matches ZXing’s encoding, we rely on the encoded content length:
                    // If the payload is numeric and even-length, we *likely* are in Code C (half digits).
                    var digitsOnly = new string((payload ?? "").Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(payload) && payload.Length == digitsOnly.Length && payload.Length % 2 == 0)
                    {
                        code128SymbolChars = payload.Length / 2; // Code C
                    }
                    else
                    {
                        code128SymbolChars = payload?.Length ?? 0; // rough upper bound (Code B)
                    }
                    var bars = 11 + (11 * Math.Max(0, code128SymbolChars)) + 11 /*checksum*/ + 15 /*stop+term*/;
                    return (bars, 10, 10);

                default:
                    throw new NotSupportedException($"Unsupported barcode type: {codeType}");
            }
        }

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
                case "EAN8":
                case "EAN-8":
                    {
                        // Allow 7 or 8 digits; auto-add check digit for 7.
                        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
                        if (string.IsNullOrEmpty(digits)) digits = "5512345"; // safe 7-digit sample
                        var e8 = EnsureEan8(digits);
                        return (ZXing.BarcodeFormat.EAN_8, e8);
                    }


                default:
                    return (ZXing.BarcodeFormat.CODE_128, string.IsNullOrWhiteSpace(raw) ? "123456789012" : raw);
            }
        }

        private static char ComputeEan8CheckDigit(ReadOnlySpan<char> sevenDigits)
        {
            // EAN-8 check digit: sum of (odd positions * 3) + (even positions * 1), modulo 10
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                int d = sevenDigits[i] - '0';
                if ((i % 2) == 0) // positions 1,3,5,7 (0-based even) => *3
                    sum += d * 3;
                else
                    sum += d;
            }
            int mod = sum % 10;
            int check = (10 - mod) % 10;
            return (char)('0' + check);
        }

        private static string EnsureEan8(string code)
        {
            // Accepts:
            //  - 7 digits: auto-append check digit
            //  - 8 digits: returns as-is (assumes caller knows)
            // Trims and strips spaces/hyphens.
            var raw = new string(code.Where(char.IsDigit).ToArray());
            if (raw.Length == 7)
                return raw + ComputeEan8CheckDigit(raw);
            if (raw.Length == 8)
                return raw;
            throw new ArgumentException("EAN-8 must be 7 or 8 digits.", nameof(code));
        }

    }
}
