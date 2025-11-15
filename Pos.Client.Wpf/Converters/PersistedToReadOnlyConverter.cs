using System;
using System.Globalization;
using System.Windows.Data;

namespace Pos.Client.Wpf.Converters
{
    // Returns true (read-only) when Id > 0 (persisted); false when Id == 0 (staged/new)
    public sealed class PersistedToReadOnlyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = 0;
            if (value is int i) id = i;
            else if (value is int?) id = ((int?)value) ?? 0;
            return id > 0; // read-only if persisted
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
