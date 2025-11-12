using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public sealed class LevelToIndentConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var level = 0;
            if (values != null && values.Length > 0 && values[0] is int i) level = i;
            var indent = Math.Max(0, level) * 16;
            return new Thickness(indent, 0, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
