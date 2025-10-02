// Pos.Client.Wpf/Converters/BoolToTextActiveInactive.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters   // ← must match the xmlns mapping
{
    public sealed class BoolToTextActiveInactive : IValueConverter   // ← public
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "Active" : "Inactive";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
