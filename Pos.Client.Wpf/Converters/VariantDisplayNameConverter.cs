// Pos.Client.Wpf/Converters/VariantDisplayNameConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using Pos.Domain.Formatting;

namespace Pos.Client.Wpf.Converters
{
    public class VariantDisplayNameConverter : IMultiValueConverter
    {
        public bool PreferProductName { get; set; } = true;
        public VariantStyle VariantStyle { get; set; } = VariantStyle.ValuesOnly;
        public string VariantJoiner { get; set; } = " / ";
        public string VariantPrefix { get; set; } = " — ";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Be defensive: bindings may pass fewer than 6 values or nulls.
            string? productName = GetStr(values, 0);
            string itemName = GetStr(values, 1) ?? string.Empty;
            string? v1Name = GetStr(values, 2);
            string? v1Val = GetStr(values, 3);
            string? v2Name = GetStr(values, 4);
            string? v2Val = GetStr(values, 5);

            var opts = new ProductNameOptions
            {
                PreferProductName = PreferProductName,
                VariantStyle = VariantStyle,
                VariantJoiner = VariantJoiner,
                VariantPrefix = VariantPrefix
            };

            try
            {
                return ProductNameComposer.Compose(productName, itemName, v1Name, v1Val, v2Name, v2Val, opts)
                       ?? (productName ?? itemName);
            }
            catch
            {
                // Absolute fallback: never throw from a converter
                return productName ?? itemName;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static string? GetStr(object[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
                return null;

            return values[index] switch
            {
                null => null,
                string s => s,
                _ => values[index]?.ToString()
            };
        }
    }
}
