using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Pos.Client.Wpf.Converters
{
    public sealed class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string ?? "").ToLowerInvariant();
            return s switch
            {
                "error" => new SolidColorBrush(Color.FromRgb(0xFF, 0xE5, 0xE5)),
                "saved" => new SolidColorBrush(Color.FromRgb(0xE8, 0xFF, 0xE8)),
                "valid" => new SolidColorBrush(Color.FromRgb(0xF5, 0xFF, 0xF5)),
                _ => Brushes.White
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
