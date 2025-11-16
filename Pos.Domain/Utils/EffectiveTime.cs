namespace Pos.Domain.Utils
{
    public static class EffectiveTime
    {
        /// <summary>
        /// Compose a LOCAL DateTime from a date (local) and the current local time-of-day.
        /// </summary>
        public static DateTime ComposeLocalFromDateAndNowTime(DateTime dateOnlyLocal)
        {
            if (dateOnlyLocal.Kind == DateTimeKind.Utc)
                dateOnlyLocal = dateOnlyLocal.ToLocalTime();

            var now = DateTime.Now; // local
            return new DateTime(
                dateOnlyLocal.Year, dateOnlyLocal.Month, dateOnlyLocal.Day,
                now.Hour, now.Minute, now.Second, DateTimeKind.Local);
        }

        /// <summary>
        /// Same as above but returns UTC.
        /// </summary>
        public static DateTime ComposeUtcFromDateAndNowTime(DateTime dateOnlyLocal)
            => ComposeLocalFromDateAndNowTime(dateOnlyLocal).ToUniversalTime();

        /// <summary>
        /// If you later add a TimePicker, use this overload.
        /// </summary>
        public static DateTime ComposeUtcFromDateAndTime(DateTime dateOnlyLocal, TimeSpan timeOfDayLocal)
        {
            if (dateOnlyLocal.Kind == DateTimeKind.Utc)
                dateOnlyLocal = dateOnlyLocal.ToLocalTime();

            var local = new DateTime(
                dateOnlyLocal.Year, dateOnlyLocal.Month, dateOnlyLocal.Day,
                timeOfDayLocal.Hours, timeOfDayLocal.Minutes, timeOfDayLocal.Seconds,
                DateTimeKind.Local);

            return local.ToUniversalTime();
        }
    }
}
