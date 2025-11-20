using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    public sealed class NegateBooleanConverter : IValueConverter
    {
        public static readonly NegateBooleanConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : Binding.DoNothing;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : Binding.DoNothing;
    }
}
