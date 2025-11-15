using System;
using System.Globalization;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Converters
{
    public class ProductPrimaryToThumbConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Product p)
                return null;

            // Prefer DI via App.Services to avoid EF in the converter
            var sp = App.Services;
            if (sp is null) return null;

            var media = sp.GetRequiredService<IProductMediaService>();
            return media.GetPrimaryThumbPath(p.Id);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
