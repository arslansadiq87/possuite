// Pos.Client.Wpf/Printing/Preview/SkiaPreviewRenderer.cs
using SkiaSharp;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.UI.ViewManagement;

public static class SkiaPreviewRenderer
{
    public static BitmapSource Render(ReceiptLayout layout)
    {
        var width = layout.PaperWidthDots;
        int y = 0;

        // Pre-measure height (quick-and-dirty)
        int height = 20;
        foreach (var b in layout.Blocks)
        {
            height += b switch
            {
                TextBlockRun t => (int)(MeasureTextHeight(t)),    // implement with SKPaint
                RuleBlock r => r.ThicknessPx + 6,
                SpacerBlock s => s.HeightPx,
                BarcodeBlock bc => bc.Symbology == BarcodeSymbology.Qr ? width : bc.HeightPx + 4,
                ImageBlock ib => (ib.TargetWidthPx ?? width) * 2 / 5, // estimate
                _ => 10
            };
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, Math.Max(height, 400), SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.White);

        using var mono = new SKPaint { Typeface = SKTypeface.FromFamilyName("Consolas"), TextSize = 20, IsAntialias = false, Color = SKColors.Black };
        using var prop = new SKPaint { Typeface = SKTypeface.FromFamilyName("Segoe UI"), TextSize = 20, IsAntialias = true, Color = SKColors.Black };

        foreach (var b in layout.Blocks)
        {
            switch (b)
            {
                case TextBlockRun t:
                    var paint = t.Mono ? mono : prop;
                    paint.FakeBoldText = t.Bold;
                    paint.TextSize = (float)(t.FontSizePt ?? 20) * (float)t.ScaleY;
                    float textWidth = paint.MeasureText(t.Text) * (float)t.ScaleX;
                    float x = t.Align switch
                    {
                        TextAlign.Center => (width - textWidth) / 2f,
                        TextAlign.Right => (width - textWidth) - 0,
                        _ => 0
                    };
                    c.Save();
                    c.Scale((float)t.ScaleX, (float)t.ScaleY, x, y);
                    c.DrawText(t.Text, x, y + paint.TextSize, paint);
                    c.Restore();
                    y += (int)(paint.TextSize + 6);
                    break;

                case RuleBlock r:
                    c.DrawRect(new SKRect(0, y, width, y + r.ThicknessPx), new SKPaint { Color = SKColors.Black });
                    y += r.ThicknessPx + 6;
                    break;

                case SpacerBlock s:
                    y += s.HeightPx;
                    break;

                case ImageBlock ib:
                    using (var img = SKBitmap.Decode(ib.PixelsOrPng))
                    {
                        int targetW = ib.TargetWidthPx ?? Math.Min(img.Width, width);
                        float scale = (float)targetW / img.Width;
                        int targetH = (int)(img.Height * scale);
                        float xi = ib.Align switch
                        {
                            TextAlign.Center => (width - targetW) / 2f,
                            TextAlign.Right => width - targetW,
                            _ => 0
                        };
                        c.DrawBitmap(img, new SKRect(xi, y, xi + targetW, y + targetH));
                        y += targetH + 6;
                    }
                    break;

                case BarcodeBlock bc:
                    // Use ZXing.Net to generate QR/Code128 bitmap, then draw with SKCanvas
                    var bmp = BarcodeHelper.Render(bc, width); // implement with ZXing.Net
                    float xb = bc.Align == TextAlign.Center ? (width - bmp.Width) / 2f : bc.Align == TextAlign.Right ? width - bmp.Width : 0f;
                    c.DrawBitmap(bmp, xb, y);
                    y += bmp.Height + 6;
                    bmp.Dispose();
                    break;
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        var wb = new WriteableBitmap(BitmapFrame.Create(ms));
        return wb;
    }
}
