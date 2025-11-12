using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.DTO.Security;
using Pos.Domain.Services.Security;

namespace Pos.Client.Wpf.Security
{
    /// <summary>
    /// Centralized access control helper for the WPF client.
    /// Delegates to IAuthService and IAuthorizationService under the hood.
    /// </summary>
    public static class AuthZ
    {
        private static IAuthService Auth => App.Services.GetRequiredService<IAuthService>();
        private static IAuthorizationService Policy => App.Services.GetRequiredService<IAuthorizationService>();
        private static AppState State => App.Services.GetRequiredService<AppState>();

        /// <summary>
        /// Gets the currently logged-in user (cached via AuthService).
        /// </summary>
        public static Task<UserInfoDto?> CurrentUserAsync(CancellationToken ct = default)
            => Auth.GetCurrentUserAsync(ct);

        private static async Task<UserInfoDto?> GetUserAsync(CancellationToken ct)
            => await Auth.GetCurrentUserAsync(ct);

        private static int? CurrentOutletId => State.CurrentOutletId > 0 ? State.CurrentOutletId : (int?)null;

        // =========================
        // ROLE CHECK HELPERS
        // =========================

        public static async Task<bool> IsAdminAsync(CancellationToken ct = default)
        {
            var user = await GetUserAsync(ct);
            return await Policy.IsAdminAsync(user, CurrentOutletId, ct);
        }

        public static async Task<bool> IsManagerOrAboveAsync(CancellationToken ct = default)
        {
            var user = await GetUserAsync(ct);
            return await Policy.IsManagerOrAboveAsync(user, CurrentOutletId, ct);
        }

        public static async Task<bool> IsSupervisorOrAboveAsync(CancellationToken ct = default)
        {
            var user = await GetUserAsync(ct);
            return await Policy.IsSupervisorOrAboveAsync(user, CurrentOutletId, ct);
        }

        public static async Task<bool> IsCashierOrAboveAsync(CancellationToken ct = default)
        {
            var user = await GetUserAsync(ct);
            return await Policy.IsCashierOrAboveAsync(user, CurrentOutletId, ct);
        }

        // =========================
        // PERMISSION CHECK
        // =========================

        public static async Task<bool> HasAsync(Perm perm, CancellationToken ct = default)
        {
            var user = await GetUserAsync(ct);
            return await Policy.HasAsync(user, perm, CurrentOutletId, ct);
        }

        public static bool IsAdminCached()
        {
            var auth = App.Services.GetRequiredService<IAuthService>();
            var policy = App.Services.GetRequiredService<IAuthorizationService>();
            var state = App.Services.GetRequiredService<AppState>();

            var user = auth.GetCurrentUserAsync().GetAwaiter().GetResult(); // only safe if it's in-memory cached
            var outletId = state.CurrentOutletId > 0 ? state.CurrentOutletId : (int?)null;
            return policy.IsAdminAsync(user, outletId).GetAwaiter().GetResult();
        }

    }
}
