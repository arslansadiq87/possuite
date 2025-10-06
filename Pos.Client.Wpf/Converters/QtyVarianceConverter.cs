// Pos.Client.Wpf/Converters/QtyVarianceConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    /// <summary>
    /// MultiBinding converter for transfer variances.
    /// Values[0] = QtyExpected (decimal)
    /// Values[1] = QtyReceived (nullable decimal)
    /// ConverterParameter: "short" or "over"
    /// </summary>
    public sealed class QtyVarianceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values is not { Length: >= 2 }) return 0m;

                var expected = values[0] is decimal de ? de : ParseDecimal(values[0], 0m);
                var received = values[1] is decimal dr ? dr
                              : values[1] is null ? 0m
                              : ParseDecimal(values[1], 0m);

                var mode = (parameter as string)?.Trim().ToLowerInvariant();

                return mode switch
                {
                    "short" => Math.Max(expected - received, 0m),
                    "over" => Math.Max(received - expected, 0m),
                    _ => 0m
                };
            }
            catch
            {
                return 0m;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static decimal ParseDecimal(object? v, decimal fallback)
            => v is IFormattable f
               && decimal.TryParse(f.ToString(null, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                   ? d : fallback;
    }
}
