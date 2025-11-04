// Pos.Client.Wpf/Services/TimeService.cs
using System;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Central place to convert UTC → user-selected display time zone.
    /// </summary>
    public static class TimeService
    {
        private static TimeZoneInfo _tz = TimeZoneInfo.Local; // fallback

        public static string CurrentTimeZoneId => _tz.Id;
        public static void SetTimeZone(string? tzId)
        {
            if (string.IsNullOrWhiteSpace(tzId))
            {
                _tz = TimeZoneInfo.Local;
                return;
            }
            try { _tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch { _tz = TimeZoneInfo.Local; }
        }

        /// <summary>Converts a UTC DateTime to the display zone. If kind is Local/Unspecified, assumes the value is UTC.</summary>
        public static DateTime ToDisplay(DateTime utc)
        {
            var u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(u, _tz);
        }

        public static string Format(DateTime utc, string format = "yyyy-MM-dd HH:mm")
            => ToDisplay(utc).ToString(format);
    }
}
