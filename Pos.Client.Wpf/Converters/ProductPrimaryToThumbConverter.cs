using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Converters
{
    public class ProductPrimaryToThumbConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Product p || parameter is not IDbContextFactory<PosClientDbContext> dbf)
                return null;

            using var db = dbf.CreateDbContext();
            return db.ProductImages.AsNoTracking()
                .Where(x => x.ProductId == p.Id && x.IsPrimary)
                .Select(x => x.LocalThumbPath)
                .FirstOrDefault();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
