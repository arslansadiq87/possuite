//Pos.Client.Wpf/Converters.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Pos.Client.Wpf
{
    public class NegativeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is IConvertible c && c.ToDecimal(culture) < 0m;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class DiscountActiveToBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                decimal pct = 0m, amt = 0m;
                if (values[0] is IConvertible c1) pct = c1.ToDecimal(culture);
                if (values[1] is IConvertible c2) amt = c2.ToDecimal(culture);
                if (pct > 0m || amt > 0m) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10893E"));
            }
            catch { }
            return Brushes.Black;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // You can put this in the same file (outside the class) or in a shared Converters.cs
    public sealed class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
    }

}
