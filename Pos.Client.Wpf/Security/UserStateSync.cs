using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.Services.Security;

namespace Pos.Client.Wpf.Security
{
    /// <summary>Synchronizes AppState from the authenticated user via the service layer.</summary>
    public static class UserStateSync
    {
        public static async Task SyncAsync(CancellationToken ct = default)
        {
            var auth = App.Services.GetRequiredService<IAuthService>();
            var state = App.Services.GetRequiredService<AppState>();

            var user = await auth.GetCurrentUserAsync(ct);
            if (user is not null)
            {
                state.CurrentUserId = user.Id;
                state.CurrentUserName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
            }
            else
            {
                state.CurrentUserId = state.CurrentUserId > 0 ? state.CurrentUserId : 0;
                state.CurrentUserName = string.IsNullOrWhiteSpace(state.CurrentUserName) ? "admin" : state.CurrentUserName;
            }
        }
    }
}
