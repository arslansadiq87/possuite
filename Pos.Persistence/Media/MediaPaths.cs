// Pos.Persistence/Media/MediaPaths.cs
using System.IO;

namespace Pos.Persistence.Media
{
    /// <summary>
    /// Centralized local paths for media staging (originals) and fast POS thumbnails.
    /// Lives in Persistence so both Persistence and WPF can consume without cross-layer refs.
    /// </summary>
    public static class MediaPaths
    {
        public static string BaseDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                         "PosSuite", "media");

        public static string OriginalsDir => Path.Combine(BaseDir, "originals");
        public static string ThumbsDir => Path.Combine(BaseDir, "thumbs");

        public static void Ensure()
        {
            Directory.CreateDirectory(OriginalsDir);
            Directory.CreateDirectory(ThumbsDir);
        }
    }
}
