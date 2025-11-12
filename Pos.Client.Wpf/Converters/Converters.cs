using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    public sealed class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Handles nulls gracefully, string or numeric comparisons
            var s1 = value?.ToString() ?? string.Empty;
            var s2 = parameter?.ToString() ?? string.Empty;
            return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // If checkbox/radio returns true, emit parameter string; else keep binding unchanged
            if (value is bool b && b)
                return parameter?.ToString() ?? string.Empty;
            return Binding.DoNothing;
        }
    }

    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Null-safe inverse
            return !(value as bool? ?? false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Keep symmetry: same inverse behavior
            return !(value as bool? ?? false);
        }
    }
}
