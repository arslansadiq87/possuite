// Pos.Client.Wpf/Converters/NullableIntConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    public sealed class NullableIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? i.ToString(culture) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            if (string.IsNullOrEmpty(s)) return null;        // allow clearing → null in model
            if (int.TryParse(s, NumberStyles.Integer, culture, out var n)) return n;
            return Binding.DoNothing;                        // ignore invalid keystroke
        }
    }
}
