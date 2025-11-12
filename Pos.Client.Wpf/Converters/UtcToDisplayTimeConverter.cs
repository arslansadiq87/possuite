// Pos.Client.Wpf/Converters/UtcToDisplayTimeConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Converters
{
    public sealed class UtcToDisplayTimeConverter : IValueConverter
    {
        // ConverterParameter may be a .NET format string (e.g., "yyyy-MM-dd HH:mm")
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null)
                return string.Empty;

            var fmt = parameter as string ?? "yyyy-MM-dd HH:mm";

            DateTime dt;

            // ---- normalize supported input types ----
            if (value is DateTime d1)
            {
                dt = d1;
            }
            else if (value is DateTime nullableDt && nullableDt != default)
            {
                dt = nullableDt;
            }
            else if (value is DateTimeOffset dto)
            {
                dt = dto.UtcDateTime;
            }
            else if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                if (!DateTime.TryParse(
                        s,
                        culture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out dt) &&
                    !DateTime.TryParse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out dt))
                {
                    return s; // leave string unchanged if unparsable
                }
            }
            else
            {
                return value; // unsupported type
            }

            // ---- safe format output ----
            try
            {
                return TimeService.Format(dt, fmt);
            }
            catch
            {
                try { return dt.ToString(fmt, culture); }
                catch { return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
