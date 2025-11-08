// Pos.Client.Wpf/Services/MediaPaths.cs
using System.IO;

namespace Pos.Client.Wpf.Services
{
    public static class MediaPaths
    {
        public static string BaseDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PosSuite", "media");

        public static string OriginalsDir => Path.Combine(BaseDir, "originals");
        public static string ThumbsDir => Path.Combine(BaseDir, "thumbs");

        public static void Ensure()
        {
            Directory.CreateDirectory(OriginalsDir);
            Directory.CreateDirectory(ThumbsDir);
        }
    }
}
