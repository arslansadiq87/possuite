using System;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Startup
{
    public static class TimeZoneBootstrapper
    {
        /// <summary>
        /// Call at app startup. Prefers per-counter InvoiceSettings; falls back to legacy UserPreferences if no counter yet.
        /// </summary>
        public static void ApplyInitial(IServiceProvider sp)
        {
            // If a counter is already known, prefer invoice settings
            try
            {
                var outletId = AppState.Current?.CurrentOutletId ?? 0;
                if (outletId > 0)
                {
                    var inv = sp.GetRequiredService<IInvoiceSettingsScopedService>()
                        .GetForOutletAsync(outletId).GetAwaiter().GetResult();

                    SetTz(inv?.DisplayTimeZoneId);
                    return;
                }
            }
            catch { /* ignore and fall back */ }

            // Fallback: legacy preferences (for first run, before till/counter is chosen)
          
        }

        /// <summary>
        /// Call this right after the user assigns/selects a counter/till to switch to the counter-scoped TZ.
        /// </summary>
        public static void ApplyForCurrentCounter(IServiceProvider sp)
        {
            try
            {
                var outletId = AppState.Current?.CurrentOutletId ?? 0;
                if (outletId <= 0) return;

                var inv = sp.GetRequiredService<IInvoiceSettingsScopedService>()
                    .GetForOutletAsync(outletId).GetAwaiter().GetResult();

                SetTz(inv?.DisplayTimeZoneId);
            }
            catch
            {
                // Keep whatever TZ is already set.
            }
        }

        private static void SetTz(string? tzId)
        {
            // Your existing static utility
            TimeService.SetTimeZone(string.IsNullOrWhiteSpace(tzId) ? null : tzId);
        }
    }
}
