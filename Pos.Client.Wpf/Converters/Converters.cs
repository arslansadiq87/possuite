using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    public sealed class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value as bool?) == true ? parameter?.ToString() : Binding.DoNothing;
    }

    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value as bool? ?? false);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value as bool? ?? false);
    }
}
