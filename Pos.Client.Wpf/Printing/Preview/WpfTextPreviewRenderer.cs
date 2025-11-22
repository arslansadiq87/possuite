// Pos.Client.Wpf/Printing/Preview/WpfTextPreviewRenderer.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pos.Client.Wpf.Printing.Preview
{
    /// <summary>
    /// Renders monospaced receipt text into a bitmap whose width equals the printer paper width (in dots).
    /// It measures the widest line at a base size, computes a size factor so that line fits exactly,
    /// and draws all lines at the adjusted font size. No non-uniform scaling → no stretching.
    /// </summary>
    public static class WpfTextPreviewRenderer
    {
        /// <summary>
        /// Main API: render text to a bitmap with width == paperWidthDots (384 for 58mm, 576 for 80mm).
        /// </summary>
        /// <param name="text">Receipt text with '\n' line breaks; monospaced alignment is respected.</param>
        /// <param name="paperWidthDots">Target bitmap width in device pixels (thermal printer dots).</param>
        /// <param name="dpi">Bitmap DPI. 96 is standard; keep Image.Stretch=None for pixel-accurate preview.</param>
        /// <param name="baseFontSize">Starting font size (points, WPF units). Will be scaled automatically.</param>
        /// <param name="fontFamily">Monospace font family (e.g., Consolas).</param>
        public static ImageSource RenderFromTextAuto(
            string? text,
            int paperWidthDots,
            double dpi = 96.0,
            double baseFontSize = 14.0,
            string fontFamily = "Consolas")
        {
            text ??= string.Empty;

            // Normalize newlines and split
            var lines = text.Replace("\r\n", "\n").Split('\n');

            // Convert target width from device pixels to WPF DIPs (96 DIPs per inch)
            double paperWidthDips = paperWidthDots * (96.0 / dpi);

            // Typeface + a probe to derive base line height
            var typeface = new Typeface(new FontFamily(fontFamily),
                                        FontStyles.Normal,
                                        FontWeights.Normal,
                                        FontStretches.Normal);
            double pixelsPerDip = 1.0;

            // Measure base line height once
            var probe = new FormattedText("A",
                                          CultureInfo.InvariantCulture,
                                          FlowDirection.LeftToRight,
                                          typeface,
                                          baseFontSize,
                                          Brushes.Black,
                                          pixelsPerDip);
            double baseLineHeight = probe.Height + 2.0; // +2 px interline spacing

            // Measure widest line at base size
            double widest = 1.0;
            for (int i = 0; i < lines.Length; i++)
            {
                var ft = new FormattedText(lines[i],
                                           CultureInfo.InvariantCulture,
                                           FlowDirection.LeftToRight,
                                           typeface,
                                           baseFontSize,
                                           Brushes.Black,
                                           pixelsPerDip);
                double w = ft.WidthIncludingTrailingWhitespace;
                if (w > widest) widest = w;
            }
            if (widest <= 0) widest = 1.0;

            // Compute scale factor so the widest line fits the paper width exactly
            double sizeFactor = paperWidthDips / widest;

            // Adjusted font size and line height
            double fontSize = baseFontSize * sizeFactor;
            double lineHeight = baseLineHeight * sizeFactor;

            // Total height (in DIPs)
            double totalHeightDips = Math.Max(1, lines.Length) * lineHeight;

            // Draw into a DrawingVisual at exact paper width
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // White background across the whole paper width
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, paperWidthDips, totalHeightDips));

                // Hint text rendering for crisper mono display
                TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
                RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);

                double y = 0.0;
                for (int i = 0; i < lines.Length; i++)
                {
                    var ft = new FormattedText(lines[i],
                                               CultureInfo.InvariantCulture,
                                               FlowDirection.LeftToRight,
                                               typeface,
                                               fontSize,
                                               Brushes.Black,
                                               pixelsPerDip);
                    // Left aligned; monospaced layout preserved by the builder
                    dc.DrawText(ft, new Point(0, y));
                    y += lineHeight;
                }
            }

            // Render to an RTB of exact device-pixel width/height
            int pixelWidth = paperWidthDots;
            int pixelHeight = (int)Math.Ceiling(totalHeightDips * dpi / 96.0);

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Back-compat wrapper so older callers using (widthCols, paperWidthDots) still compile.
        /// widthCols is ignored; auto-scaler measures the widest actual line instead.
        /// </summary>
        public static ImageSource RenderFromTextCols(
            string? text,
            int widthCols,
            int paperWidthDots,
            double dpi = 96.0,
            double baseFontSize = 14.0,
            string fontFamily = "Consolas")
        {
            return RenderFromTextAuto(text, paperWidthDots, dpi, baseFontSize, fontFamily);
        }

        // Pos.Client.Wpf/Printing/Preview/WpfTextPreviewRenderer.cs
        static double ClampEm(double v, double min = 1.0) => v < min ? min : v;

        public static ImageSource RenderFromTextWithLogo(
    string? text,
    byte[]? logoPng,
    int paperWidthDots,
    int? maxLogoWidthDots,
    int topMarginLines,
    int? businessNameFontSizePt,
    bool businessNameBold,
    string? logoAlignment,                // NEW
    bool allTextBold,                 // NEW
    double dpi = 96.0,
    double baseFontSize = 14.0,
    string fontFamily = "Consolas")
        {
            static double ClampEm(double v, double min = 1.0) => v < min ? min : v;

            text ??= string.Empty;

            // ── Load logo (optional) ───────────────────────────────────────────────
            BitmapSource? logo = null;
            if (logoPng is { Length: > 0 })
            {
                var bmp = new BitmapImage();
                using var ms = new System.IO.MemoryStream(logoPng);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                logo = bmp;
            }

            // ── Prep & measurements ────────────────────────────────────────────────
            var lines = text.Replace("\r\n", "\n").Split('\n');
            double paperWidthDips = paperWidthDots * (96.0 / dpi);

            var typeface = new Typeface(new FontFamily(fontFamily),
                                        FontStyles.Normal,
                                        FontWeights.Normal,
                                        FontStretches.Normal);
            double pixelsPerDip = 1.0;

            // Probe default line height at base size
            var probe = new FormattedText("A",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface, baseFontSize, Brushes.Black, pixelsPerDip);
            double baseLine = probe.Height + 2.0;


            // Widest line at base size to compute scale
            double widest = 1.0;
            foreach (var line in lines)
            {
                var ft = new FormattedText(line,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface, baseFontSize, Brushes.Black, pixelsPerDip);
                widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
            }
            if (widest <= 0) widest = 1.0;

            // Scale → body font/line height
            double sizeFactor = paperWidthDips / widest;
            double textFontSize = ClampEm(baseFontSize * sizeFactor, 1.0);
            double lineHeight = baseLine * sizeFactor;

            // Business Name style
            var bnWeight = businessNameBold ? FontWeights.Bold : FontWeights.Normal;
            double bnFontSize = (businessNameFontSizePt is int pt && pt > 0)
                ? ClampEm(pt * (96.0 / 72.0), 1.0)   // pt → DIPs
                : ClampEm(textFontSize * 1.15, 1.0);

            // First non-empty line is Business Name
            int bnIndex = Array.FindIndex(lines, s => !string.IsNullOrWhiteSpace(s));
            if (bnIndex < 0) bnIndex = 0;

            // Pre-measure BN height (use trimmed text for true visual height)
            string bnTrim = (bnIndex >= 0 && bnIndex < lines.Length)
                ? (lines[bnIndex] ?? string.Empty).Trim()
                : string.Empty;

            var bnFt = new FormattedText(
                bnTrim,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(fontFamily), FontStyles.Normal, bnWeight, FontStretches.Normal),
                bnFontSize,
                Brushes.Black,
                pixelsPerDip);

            double bnHeight = bnFt.Height;
            double bnExtra = Math.Max(0, bnHeight - lineHeight);

            // Logo sizing (centered, width-capped)
            double logoHeightDips = 0.0, logoWidthDips = 0.0;
            if (logo != null)
            {
                int capDots = Math.Min(paperWidthDots, Math.Max(1, maxLogoWidthDots ?? paperWidthDots));
                double capWidthDips = capDots * (96.0 / dpi);
                double scale = capWidthDips / logo.PixelWidth;
                logoWidthDips = capWidthDips;
                logoHeightDips = (logo.PixelHeight * scale) * (96.0 / dpi);
            }

            // Top margin in DIPs (based on body line height)
            double topMarginDips = Math.Max(0, topMarginLines) * lineHeight;

            // Total height: margin + logo + lines + BN extra
            double totalHeightDips =
                topMarginDips +
                logoHeightDips +
                (Math.Max(1, lines.Length) * lineHeight) +
                bnExtra;

            // ── Draw ───────────────────────────────────────────────────────────────
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, paperWidthDips, totalHeightDips));

                double y = 0.0;
                y += topMarginDips;

                if (logo != null)
                {
                    double xLogo;
                    var align = (logoAlignment ?? "Center").Trim().ToLowerInvariant();
                    if (align.StartsWith("l")) xLogo = 0;
                    else if (align.StartsWith("r")) xLogo = Math.Max(0, paperWidthDips - logoWidthDips);
                    else xLogo = (paperWidthDips - logoWidthDips) / 2.0;

                    dc.DrawImage(logo, new Rect(xLogo, y, logoWidthDips, logoHeightDips));
                    y += logoHeightDips;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    bool isBn = (i == bnIndex);

                    // Use trimmed content for BN so ASCII-centering spaces don’t skew width
                    string raw = lines[i] ?? string.Empty;
                    string render = isBn ? raw.Trim() : raw;
                    if (render.Length == 0) { y += lineHeight; continue; }

                    // NEW: global bold toggle
                    var weight = isBn
                        ? (businessNameBold ? FontWeights.Bold : (allTextBold ? FontWeights.Bold : FontWeights.Normal))
                        : (allTextBold ? FontWeights.Bold : FontWeights.Normal);

                    var ft = new FormattedText(
                        render,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily(fontFamily),
                                     FontStyles.Normal,
                                     weight,
                                     FontStretches.Normal),
                        ClampEm(isBn ? bnFontSize : textFontSize, 1.0),
                        Brushes.Black,
                        pixelsPerDip);

                    double x = 0.0;
                    if (isBn)
                    {
                        double w = ft.Width; // visual width of trimmed BN
                        x = Math.Round(Math.Max(0, (paperWidthDips - w) / 2.0));
                    }

                    dc.DrawText(ft, new Point(x, y));
                    y += isBn ? Math.Max(lineHeight, ft.Height) : lineHeight;
                }

            }

            int pixelWidth = paperWidthDots;
            int pixelHeight = (int)Math.Ceiling(totalHeightDips * dpi / 96.0);
            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }


    }
}
