using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    public sealed class SubtractConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Robust: accept decimal/double/float/int/long/short/string/null
            if (values == null || values.Length < 2)
                return 0m;

            if (!TryToDecimal(values[0], culture, out var a)) a = 0m;
            if (!TryToDecimal(values[1], culture, out var b)) b = 0m;

            // Return decimal result (WPF will box it)
            return a - b;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();

        private static bool TryToDecimal(object? v, IFormatProvider culture, out decimal d)
        {
            switch (v)
            {
                case null:
                    d = 0m; return false;
                case decimal m:
                    d = m; return true;
                case double db:
                    d = (decimal)db; return true;
                case float f:
                    d = (decimal)f; return true;
                case int i:
                    d = i; return true;
                case long l:
                    d = l; return true;
                case short s:
                    d = s; return true;
                case string s when !string.IsNullOrWhiteSpace(s):
                    return decimal.TryParse(s, NumberStyles.Number, culture, out d);
                default:
                    try { d = System.Convert.ToDecimal(v, culture); return true; }
                    catch { d = 0m; return false; }
            }
        }
    }

    public sealed class JournalDiffVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value?.ToString() ?? "Journal";
            return type == "Journal" ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
