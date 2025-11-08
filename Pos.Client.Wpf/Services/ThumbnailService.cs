// Pos.Client.Wpf/Services/ThumbnailService.cs
using System;
using System.IO;
using System.Windows.Media.Imaging;
using Pos.Persistence.Media;
namespace Pos.Client.Wpf.Services
{
    public sealed class ThumbnailService
    {
        // Reasonable default for list/grid in POS
        private const int MaxThumbSize = 320; // px, max of width/height

        public string CreateThumb(string sourcePath, string fileStem)
        {
            MediaPaths.Ensure();

            using var srcStream = File.OpenRead(sourcePath);
            var decoder = BitmapDecoder.Create(srcStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            var scale = Math.Min(1.0, (double)MaxThumbSize / Math.Max(frame.PixelWidth, frame.PixelHeight));
            int w = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
            int h = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));

            var resized = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(w / (double)frame.PixelWidth, h / (double)frame.PixelHeight));

            var enc = new JpegBitmapEncoder(); // jpg for speed/size
            enc.QualityLevel = 85;
            enc.Frames.Add(BitmapFrame.Create(resized));

            var thumbPath = Path.Combine(MediaPaths.ThumbsDir, $"{fileStem}.jpg");
            using var outStream = File.Create(thumbPath);
            enc.Save(outStream);
            return thumbPath;
        }
    }
}
