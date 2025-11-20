using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace Pos.Client.Wpf.Windows.Settings
{
    /// <summary>
    /// Renders a label preview (WPF ImageSource) with:
    /// - An absolutely-positioned barcode block (by margins & optional fixed height).
    /// - Free-positioned text fields (Name, Price, SKU, Business) in mm from label TL.
    /// - Per-field alignment (Left | Center | Right) that keeps content centered/right-aligned
    ///   inside the remaining width to the label’s right edge.
    ///
    /// Units:
    ///   Input coordinates are in millimeters; WPF draws in DIPs (96 per inch).
    ///   Barcode bitmap is sized in device pixels to match the target DIP rect @ DPI.
    /// </summary>
    public static class BarcodePreviewBuilder
    {
        // mm → px helper (not strictly required in this file but kept for parity)
        private static double PxPerMm(int dpi) => dpi / 25.4;

        /// <summary>
        /// Pixels per module heuristic for 1D symbologies (kept for future tuning).
        /// </summary>
        private static int GetPixelsPerModule(int dpi)
        {
            if (dpi <= 0) return 3;
            return dpi switch
            {
                203 => 3, // ~0.375 mm/module
                300 => 4, // ~0.339 mm/module
                _ => Math.Max(3, (int)Math.Round(dpi / 25.4 / 3.03)) // ~0.33 mm/module
            };
        }

        /// <summary>
        /// Returns (barModules, quietLeftModules, quietRightModules). Useful if you derive expected widths.
        /// For Code128, approximate symbol chars are computed in out param.
        /// </summary>
        private static (int bars, int ql, int qr) GetSymbologyModuleSpec(
            string codeType, string payload, out int code128SymbolChars)
        {
            code128SymbolChars = 0;
            switch ((codeType ?? "").ToUpperInvariant())
            {
                case "EAN13":
                case "EAN-13":
                    // 95 modules; quiet zones 11X each side
                    return (95, 11, 11);

                case "UPCA":
                case "UPC-A":
                    // 95 modules; quiet zones 9X each side
                    return (95, 9, 9);

                case "EAN8":
                case "EAN-8":
                    // 67 modules; quiet zones 7X each side
                    return (67, 7, 7);

                case "CODE128":
                case "CODE 128":
                    // Each symbol consumes 11 modules. Start=11, checksum=11, stop+term=15.
                    var digitsOnly = new string((payload ?? "").Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(payload) && payload.Length == digitsOnly.Length && payload.Length % 2 == 0)
                        code128SymbolChars = payload.Length / 2; // likely Code C
                    else
                        code128SymbolChars = payload?.Length ?? 0; // rough for Code B
                    var bars = 11 + (11 * Math.Max(0, code128SymbolChars)) + 11 + 15;
                    return (bars, 10, 10);

                default:
                    throw new NotSupportedException($"Unsupported barcode type: {codeType}");
            }
        }

        /// <summary>
        /// Build a WPF ImageSource preview for the given label + content.
        /// </summary>
        public static ImageSource Build(
            // Label geometry (mm / dpi)
            int labelWidthMm, int labelHeightMm,
            int marginLeftMm, int marginTopMm, // kept for compatibility (not used by barcode layout)
            int dpi,

            // Barcode
            string codeType, string payload,

            // Text fields (content & visibility)
            bool showName, bool showPrice, bool showSku,
            string nameText, string priceText, string skuText,
            int fontSizePt,
            double nameXmm, double nameYmm,
            double priceXmm, double priceYmm,
            double skuXmm, double skuYmm,

            // Barcode block (mm)
            double barcodeMarginLeftMm, double barcodeMarginTopMm,
            double barcodeMarginRightMm, double barcodeMarginBottomMm,
            double barcodeHeightMm,

            // Business name (content + visibility + position in mm)
            bool showBusinessName, string businessName, double businessXmm, double businessYmm,

            // Alignments (Left | Center | Right)
            string nameAlign = "Left",
            string priceAlign = "Left",
            string skuAlign = "Left",
            string businessAlign = "Left",

            // NEW: horizontal zoom for barcode width (1.0 = fill area width)
            double barcodeZoom = 1.0
        )
        {
            // ---- Unit conversions
            double dipPerMm = 96.0 / 25.4;  // mm → DIP
            double pxPerDip = dpi / 96.0;   // DIP → px

            // ---- Label size (DIP)
            double widthDip = Math.Max(32, labelWidthMm * dipPerMm);
            double heightDip = Math.Max(32, labelHeightMm * dipPerMm);

            // ---- Compute barcode area from margins/height (all in DIP)
            double leftDip = barcodeMarginLeftMm * dipPerMm;
            double topDip = barcodeMarginTopMm * dipPerMm;
            double rightDip = barcodeMarginRightMm * dipPerMm;
            double bottomDip = barcodeMarginBottomMm * dipPerMm;

            double bcAreaWidthDip = Math.Max(2, widthDip - leftDip - rightDip);
            double bcAreaHeightDip = (barcodeHeightMm > 0.0)
                ? Math.Min(barcodeHeightMm * dipPerMm, Math.Max(2, heightDip - topDip - bottomDip))
                : Math.Max(2, heightDip - topDip - bottomDip);

            // ---- Apply user zoom (keep sensible bounds and center inside area)
            double z = Math.Clamp(barcodeZoom, 0.3, 2.0); // 30%..200%
            double bcDrawWidthDip = Math.Max(2, Math.Min(bcAreaWidthDip * z, widthDip));   // never exceed label width
            double bcDrawHeightDip = bcAreaHeightDip;
            double bcDrawLeftDip = leftDip + (bcAreaWidthDip - bcDrawWidthDip) / 2.0;      // center horizontally
            double bcDrawTopDip = topDip;

            Rect bcRect = new Rect(
                x: Math.Clamp(bcDrawLeftDip, 0, Math.Max(0, widthDip - 2)),
                y: Math.Clamp(bcDrawTopDip, 0, Math.Max(0, heightDip - 2)),
                width: Math.Max(2, Math.Min(bcDrawWidthDip, widthDip - bcDrawLeftDip)),
                height: Math.Max(2, Math.Min(bcDrawHeightDip, heightDip - bcDrawTopDip))
            );

            // ---- ZXing bitmap size (px) → matches the barcode draw rect size
            int zxW = Math.Max(8, (int)Math.Round(bcRect.Width * pxPerDip));
            int zxH = Math.Max(8, (int)Math.Round(bcRect.Height * pxPerDip));

            // ---- Render target size (px)
            int bmpW = Math.Max(32, (int)Math.Round(widthDip * pxPerDip));
            int bmpH = Math.Max(32, (int)Math.Round(heightDip * pxPerDip));

            var dv = new DrawingVisual();
            using var dc = dv.RenderOpen();

            // Label background + gentle border (DIP)
            var labelRect = new Rect(0, 0, widthDip, heightDip);
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), labelRect);

            // Normalize format/payload (incl. EAN-8 support)
            var (format, encoded) = CoerceFormatAndPayload(codeType, payload);

            // ---- Draw barcode into its DIP rectangle
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
                    pix.Width, pix.Height,   // px
                    dpi, dpi,                // image dpi
                    PixelFormats.Bgra32, null,
                    pix.Pixels, pix.Width * 4);

                dc.DrawImage(bmp, bcRect);
            }
            catch
            {
                // Visible error block if ZXing fails
                var pen = new Pen(Brushes.IndianRed, 2);
                dc.DrawRectangle(Brushes.White, pen, bcRect);
                dc.DrawLine(pen, bcRect.TopLeft, bcRect.BottomRight);
                dc.DrawLine(pen, bcRect.BottomLeft, bcRect.TopRight);
            }

            // ---- Texts (absolute mm → DIP). Max width = to right edge (w/ small padding).
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            void DrawAtMm(string text, bool visible, double xmm, double ymm, string align)
            {
                if (!visible || string.IsNullOrWhiteSpace(text)) return;

                double xDip = xmm * dipPerMm;
                double yDip = ymm * dipPerMm;

                if (xDip > widthDip - 2 || yDip > heightDip - 2) return; // soft clamp

                double maxW = Math.Max(0, widthDip - xDip - 2);
                DrawText(dc, text, typeface, fontSizePt, Brushes.Black, xDip, yDip, maxW, align);
            }

            DrawAtMm(nameText, showName, nameXmm, nameYmm, nameAlign);
            DrawAtMm(priceText, showPrice, priceXmm, priceYmm, priceAlign);
            DrawAtMm(skuText, showSku, skuXmm, skuYmm, skuAlign);
            DrawAtMm(businessName ?? "", showBusinessName, businessXmm, businessYmm, businessAlign);

            dc.Close();

            var rtb = new RenderTargetBitmap(bmpW, bmpH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Draw formatted text at (x,y) with a max width, using the requested alignment.
        /// (x,y) is the LEFT of the layout box; alignment applies inside MaxTextWidth.
        /// </summary>
        private static void DrawText(
            DrawingContext dc, string text, Typeface tf, double fontPt,
            Brush brush, double x, double y, double maxWidthDip, string align)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            double fontDip = fontPt * (96.0 / 72.0); // pt → DIP
            double pixelsPerDip = 1.0;
            if (Application.Current?.MainWindow != null)
                pixelsPerDip = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;

            var alignment =
                align?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true ? TextAlignment.Center :
                align?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true ? TextAlignment.Right :
                TextAlignment.Left;

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
                TextAlignment = alignment
            };

            dc.DrawText(ft, new Point(x, y));
        }

        /// <summary>
        /// Coerces the human-readable code type + payload into ZXing format + usable payload.
        /// Handles:
        ///  - EAN-13 (pads/trim to 12; ZXing adds check digit)
        ///  - UPC-A  (pads/trim to 11; ZXing adds check digit)
        ///  - EAN-8  (accepts 7 (auto check) or 8 digits)
        ///  - Code 128 (default fallback)
        /// </summary>
        private static (BarcodeFormat format, string payload) CoerceFormatAndPayload(string codeType, string raw)
        {
            string digitsOnly = new(raw?.Where(char.IsDigit).ToArray());

            switch ((codeType ?? "Code128").ToUpperInvariant())
            {
                case "EAN13":
                case "EAN-13":
                    {
                        var ean = digitsOnly.PadLeft(12, '0');
                        if (ean.Length > 12) ean = ean[^12..];
                        return (BarcodeFormat.EAN_13, ean);
                    }

                case "UPCA":
                case "UPC-A":
                    {
                        var upc = digitsOnly.PadLeft(11, '0');
                        if (upc.Length > 11) upc = upc[^11..];
                        return (BarcodeFormat.UPC_A, upc);
                    }

                case "EAN8":
                case "EAN-8":
                    {
                        // Allow 7 or 8 digits; auto-add check digit for 7.
                        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
                        if (string.IsNullOrEmpty(digits)) digits = "5512345"; // safe 7-digit sample
                        var e8 = EnsureEan8(digits);
                        return (BarcodeFormat.EAN_8, e8);
                    }

                default:
                    return (BarcodeFormat.CODE_128, string.IsNullOrWhiteSpace(raw) ? "123456789012" : raw);
            }
        }

        /// <summary>
        /// Compute EAN-8 check digit for 7 digits.
        /// </summary>
        private static char ComputeEan8CheckDigit(ReadOnlySpan<char> sevenDigits)
        {
            // EAN-8 check digit: sum of (odd positions * 3) + (even positions * 1), modulo 10
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                int d = sevenDigits[i] - '0';
                if ((i % 2) == 0) // positions 1,3,5,7 (0-based even indices)
                    sum += d * 3;
                else
                    sum += d;
            }
            int mod = sum % 10;
            int check = (10 - mod) % 10;
            return (char)('0' + check);
        }

        /// <summary>
        /// Normalize an EAN-8 code string:
        ///  - 7 digits → append computed check digit
        ///  - 8 digits → returned as-is
        ///  - otherwise → ArgumentException
        /// </summary>
        private static string EnsureEan8(string code)
        {
            var raw = new string(code.Where(char.IsDigit).ToArray());
            if (raw.Length == 7)
                return raw + ComputeEan8CheckDigit(raw);
            if (raw.Length == 8)
                return raw;
            throw new ArgumentException("EAN-8 must be 7 or 8 digits.", nameof(code));
        }
    }
}
