using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Pos.Client.Wpf.Printing
{
    public static class EscPosImageEncoder
    {
        // Typical max printable widths (dots) for 203dpi thermal heads
        public static int GetMaxDotsForPaperWidthMm(int paperWidthMm)
            => paperWidthMm <= 58 ? 384 : 576;

        public static byte[] EncodeLogoPng(byte[] pngBytes, int maxWidthDots)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                return Array.Empty<byte>();

            using var ms = new MemoryStream(pngBytes);
            using var src = (Bitmap)Image.FromStream(ms);

            // Scale proportionally to max width
            double scale = Math.Min(1.0, (double)maxWidthDots / src.Width);
            int newW = Math.Max(1, (int)Math.Round(src.Width * scale));
            int newH = Math.Max(1, (int)Math.Round(src.Height * scale));

            using var resized = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.White);
                g.DrawImage(src, new Rectangle(0, 0, newW, newH));
            }

            // Convert 24bpp RGB -> 1bpp raster bytes with a simple threshold
            var raster = ToMonoRaster(resized);

            // ESC/POS Raster bit image: GS v 0 m xL xH yL yH [data]
            const byte GS = 0x1D;
            const byte v = (byte)'v';
            const byte zero = (byte)'0';
            byte m = 0x00; // normal
            ushort bytesPerRow = (ushort)raster.BytesPerRow;
            ushort rows = (ushort)raster.Rows;

            byte xL = (byte)(bytesPerRow & 0xFF);
            byte xH = (byte)((bytesPerRow >> 8) & 0xFF);
            byte yL = (byte)(rows & 0xFF);
            byte yH = (byte)((rows >> 8) & 0xFF);

            using var outMs = new MemoryStream();
            outMs.WriteByte(GS);
            outMs.WriteByte(v);
            outMs.WriteByte(zero);
            outMs.WriteByte(m);
            outMs.WriteByte(xL);
            outMs.WriteByte(xH);
            outMs.WriteByte(yL);
            outMs.WriteByte(yH);
            outMs.Write(raster.Data, 0, raster.Data.Length);
            outMs.WriteByte(0x0A); // feed one line after image
            return outMs.ToArray();
        }

        // Produce ESC/POS-ready mono raster
        private static (byte[] Data, int BytesPerRow, int Rows) ToMonoRaster(Bitmap rgb24)
        {
            int w = rgb24.Width;
            int h = rgb24.Height;
            int bytesPerRow = (w + 7) / 8;
            var data = new byte[bytesPerRow * h];

            // Lock once for fast read
            var rect = new Rectangle(0, 0, w, h);
            var bd = rgb24.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = bd.Stride;
                IntPtr scan0 = bd.Scan0;

                // Simple luma threshold (tweak if needed)
                const int threshold = 160; // 0..255 — lower = darker print
                for (int row = 0; row < h; row++)
                {
                    int rowOffset = row * bytesPerRow;
                    for (int col = 0; col < w; col++)
                    {
                        // 24bpp → BGR
                        int srcIndex = row * stride + col * 3;
                        byte b = System.Runtime.InteropServices.Marshal.ReadByte(scan0, srcIndex + 0);
                        byte g = System.Runtime.InteropServices.Marshal.ReadByte(scan0, srcIndex + 1);
                        byte r = System.Runtime.InteropServices.Marshal.ReadByte(scan0, srcIndex + 2);

                        // Perceptual luma
                        int luma = (int)(0.299 * r + 0.587 * g + 0.114 * b);

                        // ESC/POS expects 1 = black, MSB-first per byte
                        if (luma < threshold)
                        {
                            int byteIndex = rowOffset + (col >> 3);
                            int bit = 7 - (col & 7);
                            data[byteIndex] |= (byte)(1 << bit);
                        }
                    }
                }
            }
            finally
            {
                rgb24.UnlockBits(bd);
            }

            return (data, bytesPerRow, h);
        }

        public static byte[] EncodeLogoPngAligned(byte[] pngBytes, int targetWidthDots, int canvasWidthDots, string? alignment)
        {
            if (pngBytes == null || pngBytes.Length == 0) return Array.Empty<byte>();

            using var ms = new MemoryStream(pngBytes);
            using var src = (Bitmap)Image.FromStream(ms);

            // Scale proportionally to target max width
            double scale = Math.Min(1.0, (double)targetWidthDots / src.Width);
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));

            using var resized = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.White);
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }

            // Convert to mono raster for the *logo only*
            var mono = ToMonoRaster(resized); // you already have ToMonoRaster in the file

            // Build a canvas row with left padding depending on alignment
            int canvasBytesPerRow = (canvasWidthDots + 7) / 8;
            int logoBytesPerRow = mono.BytesPerRow;
            if (logoBytesPerRow > canvasBytesPerRow) logoBytesPerRow = canvasBytesPerRow;

            int xPadBytes;
            var a = (alignment ?? "Center").Trim().ToLowerInvariant();
            if (a.StartsWith("l")) xPadBytes = 0;
            else if (a.StartsWith("r")) xPadBytes = Math.Max(0, canvasBytesPerRow - logoBytesPerRow);
            else xPadBytes = Math.Max(0, (canvasBytesPerRow - logoBytesPerRow) / 2);

            var canvas = new byte[canvasBytesPerRow * mono.Rows];

            for (int row = 0; row < mono.Rows; row++)
            {
                Buffer.BlockCopy(
                    mono.Data, row * mono.BytesPerRow,
                    canvas, row * canvasBytesPerRow + xPadBytes,
                    Math.Min(logoBytesPerRow, mono.BytesPerRow));
            }

            // GS v 0 raster header built with canvas width
            const byte GS = 0x1D;
            const byte v = (byte)'v';
            const byte zero = (byte)'0';
            byte m = 0x00; // normal
            ushort x = (ushort)canvasBytesPerRow;
            ushort y = (ushort)mono.Rows;

            byte xL = (byte)(x & 0xFF);
            byte xH = (byte)((x >> 8) & 0xFF);
            byte yL = (byte)(y & 0xFF);
            byte yH = (byte)((y >> 8) & 0xFF);

            using var outMs = new MemoryStream();
            outMs.WriteByte(GS); outMs.WriteByte(v); outMs.WriteByte(zero);
            outMs.WriteByte(m);
            outMs.WriteByte(xL); outMs.WriteByte(xH);
            outMs.WriteByte(yL); outMs.WriteByte(yH);
            outMs.Write(canvas, 0, canvas.Length);
            outMs.WriteByte(0x0A); // feed
            return outMs.ToArray();
        }

    }
}
