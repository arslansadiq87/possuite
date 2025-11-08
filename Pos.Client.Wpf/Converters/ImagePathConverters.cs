// Pos.Client.Wpf/Converters/ImagePathConverters.cs
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

using Pos.Persistence.Media;

namespace Pos.Client.Wpf.Converters
{
    /// <summary>
    /// Resolves the thumbnail for an Item (variant), falling back to its Product primary.
    /// Binding usage: pass Item as value, converter param = PosClientDbContext factory.
    /// </summary>
    public class VariantThumbPathConverter : IValueConverter
    {
            public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var path = value as string;
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                try
                {
                    string? resolved = ResolveToExistingFile(path);
                    if (resolved is null) return null;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.DecodePixelWidth = 96;
                    bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch
                {
                    return null;
                }
            }

            private static string? ResolveToExistingFile(string input)
            {
                // 1) absolute file path
                if (Path.IsPathRooted(input) && File.Exists(input))
                    return Path.GetFullPath(input);

                // 2) app base dir (e.g., Pos.Client.Wpf.exe location)
                var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Normalize(input)));
                if (File.Exists(candidate)) return candidate;

                // 3) media/thumbs directory under LocalAppData\PosSuite\media
                var underThumbs = Path.Combine(MediaPaths.ThumbsDir, Normalize(Path.GetFileName(input)));
                if (File.Exists(underThumbs)) return underThumbs;

                // 4) originals directory fallback
                var underOriginals = Path.Combine(MediaPaths.OriginalsDir, Normalize(Path.GetFileName(input)));
                if (File.Exists(underOriginals)) return underOriginals;

                return null;
            }

            private static string Normalize(string p) =>
                p.Replace('/', Path.DirectorySeparatorChar)
                 .Replace('\\', Path.DirectorySeparatorChar);

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => throw new NotSupportedException();
        }
    }
