using System.Drawing;
using System.Drawing.Printing;
using System.Threading.Tasks;
using Pos.Domain.Entities;

public class LabelPrintServiceStub : ILabelPrintService
{
    public Task PrintSampleAsync(BarcodeLabelSettings s)
    {
        using var pd = new PrintDocument();
        if (!string.IsNullOrWhiteSpace(s.PrinterName))
            pd.PrinterSettings.PrinterName = s.PrinterName;

        pd.PrintPage += (sender, e) =>
        {
            var g = e.Graphics;
            if (g is null) { e.HasMorePages = false; return; }  // guard for CS8602

            // pixels per mm at target DPI
            float pxPerMm = s.Dpi / 25.4f;

            // ===== Label "page" rect (the physical sticker area) =====
            float w = (float)(s.LabelWidthMm * pxPerMm);
            float h = (float)(s.LabelHeightMm * pxPerMm);

            // Place the label at the printer’s margin origin
            var pageRect = new RectangleF(e.MarginBounds.Left, e.MarginBounds.Top, w, h);

            // Outline the label for visual debugging
            using (var pen = new Pen(Color.LightGray, 1))
                g.DrawRectangle(pen, pageRect.X, pageRect.Y, pageRect.Width, pageRect.Height);

            // ===== Barcode rect from margins/height =====
            float left = (float)(s.BarcodeMarginLeftMm * pxPerMm);
            float top = (float)(s.BarcodeMarginTopMm * pxPerMm);
            float right = (float)(s.BarcodeMarginRightMm * pxPerMm);
            float bottom = (float)(s.BarcodeMarginBottomMm * pxPerMm);

            var barcodeRect = new RectangleF(
                x: pageRect.Left + left,
                y: pageRect.Top + top,
                width: Math.Max(2f, pageRect.Width - left - right),
                height: Math.Max(2f, (s.BarcodeHeightMm > 0
                            ? (float)(s.BarcodeHeightMm * pxPerMm)
                            : pageRect.Height - top - bottom))
            );

            // Stub: visualize barcode area
            using (var penBc = new Pen(Color.Black, 1))
                g.DrawRectangle(penBc, barcodeRect.X, barcodeRect.Y, barcodeRect.Width, barcodeRect.Height);

            // ===== Text fields =====
            using var font = new Font("Arial", s.FontSizePt);

            if (s.ShowBusinessName && !string.IsNullOrWhiteSpace(s.BusinessName))
            {
                float bx = pageRect.Left + (float)(s.BusinessXmm * pxPerMm);
                float by = pageRect.Top + (float)(s.BusinessYmm * pxPerMm);
                g.DrawString(s.BusinessName, font, Brushes.Black, bx, by);
            }

            if (s.ShowName)
            {
                float nx = pageRect.Left + (float)(s.NameXmm * pxPerMm);
                float ny = pageRect.Top + (float)(s.NameYmm * pxPerMm);
                g.DrawString("Sample Item", font, Brushes.Black, nx, ny);
            }

            if (s.ShowPrice)
            {
                float px = pageRect.Left + (float)(s.PriceXmm * pxPerMm);
                float py = pageRect.Top + (float)(s.PriceYmm * pxPerMm);
                g.DrawString("Rs 999", font, Brushes.Black, px, py);
            }

            if (s.ShowSku)
            {
                float sx = pageRect.Left + (float)(s.SkuXmm * pxPerMm);
                float sy = pageRect.Top + (float)(s.SkuYmm * pxPerMm);
                g.DrawString("SKU-001", font, Brushes.Black, sx, sy);
            }
        };

        pd.Print(); // synchronous
        return Task.CompletedTask;
    }

}
