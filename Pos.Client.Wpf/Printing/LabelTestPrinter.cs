using System;
using System.Drawing;
using System.Drawing.Printing;

namespace Pos.Client.Wpf.Printing
{
    public class LabelTestConfig
    {
        public string? PrinterName { get; set; }
        public int Dpi { get; set; } = 203;
        public int LabelWidthMm { get; set; } = 38;
        public int LabelHeightMm { get; set; } = 25;
        public int MarginLeftMm { get; set; } = 2;
        public int MarginTopMm { get; set; } = 2;
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int FontSizePt { get; set; } = 9;
        public bool ShowName { get; set; } = true;
        public bool ShowPrice { get; set; } = true;
        public bool ShowSku { get; set; } = false;
        public string CodeType { get; set; } = "Code128";

        // New free-position fields (mm from top-left of label)
        public double NameXmm { get; set; } = 4.0;
        public double NameYmm { get; set; } = 18.0;
        public double PriceXmm { get; set; } = 4.0;
        public double PriceYmm { get; set; } = 22.0;
        public double SkuXmm { get; set; } = 4.0;
        public double SkuYmm { get; set; } = 26.0;
    }

    public static class LabelTestPrinter
    {
        public static void Print(LabelTestConfig cfg)
        {
            const float mmToInch = 1f / 25.4f;
            float pxPerMm = cfg.Dpi * mmToInch;

            // Convert geometry to pixels
            float labelWpx = cfg.LabelWidthMm * pxPerMm;
            float labelHpx = cfg.LabelHeightMm * pxPerMm;
            float marginLpx = cfg.MarginLeftMm * pxPerMm;
            float marginTpx = cfg.MarginTopMm * pxPerMm;

            using var pd = new PrintDocument();
            if (!string.IsNullOrWhiteSpace(cfg.PrinterName))
                pd.PrinterSettings.PrinterName = cfg.PrinterName;

            int pageW = (int)(marginLpx * 2 + cfg.Columns * labelWpx);
            int pageH = (int)(marginTpx * 2 + cfg.Rows * labelHpx);
            pd.DefaultPageSettings.PaperSize = new PaperSize("Labels", pageW, pageH);
            pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

            pd.PrintPage += (s, e) =>
            {
                var g = e.Graphics;
                if (g is null) { e.HasMorePages = false; return; } // <-- fix CS8602
                g.PageUnit = GraphicsUnit.Pixel;

                using var pen = new Pen(Color.DimGray, 1);
                using var font = new Font(FontFamily.GenericSansSerif, cfg.FontSizePt);
                using var brush = new SolidBrush(Color.Black);

                for (int r = 0; r < cfg.Rows; r++)
                {
                    for (int c = 0; c < cfg.Columns; c++)
                    {
                        float x = marginLpx + c * labelWpx;
                        float y = marginTpx + r * labelHpx;
                        var rect = new RectangleF(x, y, labelWpx, labelHpx);

                        // Outline label area
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                        // Convert text mm → px
                        float nx = rect.Left + (float)(cfg.NameXmm * pxPerMm);
                        float ny = rect.Top + (float)(cfg.NameYmm * pxPerMm);
                        float px = rect.Left + (float)(cfg.PriceXmm * pxPerMm);
                        float py = rect.Top + (float)(cfg.PriceYmm * pxPerMm);
                        float sx = rect.Left + (float)(cfg.SkuXmm * pxPerMm);
                        float sy = rect.Top + (float)(cfg.SkuYmm * pxPerMm);

                        // Draw texts according to toggles
                        if (cfg.ShowName) g.DrawString("Item Name", font, brush, nx, ny);
                        if (cfg.ShowPrice) g.DrawString("PKR 999", font, brush, px, py);
                        if (cfg.ShowSku) g.DrawString("SKU: ABC-123", font, brush, sx, sy);

                        // Placeholder barcode text (bottom)
                        float pad = 2 * pxPerMm;
                        string code = $"[{cfg.CodeType}] 123456789012";
                        float codeW = g.MeasureString(code, font).Width;
                        float tx = rect.Left + (rect.Width - codeW) / 2f;
                        g.DrawString(code, font, brush, tx, rect.Bottom - font.Height - pad);
                    }
                }
            };

            pd.Print();
        }

    }
}
