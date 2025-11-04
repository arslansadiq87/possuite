// Pos.Client.Wpf/Converters/UtcToDisplayTimeConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Converters
{
    public sealed class UtcToDisplayTimeConverter : IValueConverter
    {
        // ConverterParameter can be a .NET format string (e.g., "yyyy-MM-dd HH:mm")
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
            {
                var fmt = parameter as string ?? "yyyy-MM-dd HH:mm";
                return TimeService.Format(dt, fmt);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
