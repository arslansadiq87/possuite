//Pos.Client.Wpf/Converters/VariantDisplayNameConverter
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
            string? productName = values[0] as string;
            string itemName = values[1] as string ?? "";
            string? v1Name = values[2] as string;
            string? v1Val = values[3] as string;
            string? v2Name = values[4] as string;
            string? v2Val = values[5] as string;

            var opts = new ProductNameOptions
            {
                PreferProductName = PreferProductName,
                VariantStyle = VariantStyle,
                VariantJoiner = VariantJoiner,
                VariantPrefix = VariantPrefix
            };

            return ProductNameComposer.Compose(productName, itemName, v1Name, v1Val, v2Name, v2Val, opts);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
