// Pos.Client.Wpf/Services/AppCtx.cs (NEW - optional)
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Services
{
    public static class AppCtx
    {
        public static (int outletId, int counterId) GetOutletCounterOrThrow()
        {
            var outletId = AppState.Current.CurrentOutletId;
            var counterId = AppState.Current.CurrentCounterId;

            if (outletId <= 0 || counterId <= 0)
                throw new InvalidOperationException("Outlet/Counter not set in AppState. Ensure user is logged in and till/counter selected.");

            return (outletId, counterId);
        }
    }
}
