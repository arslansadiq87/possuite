using System;

namespace Pos.Client.Wpf.Services
{
    public static class Guards
    {
        public static void EnsureBoundCounter()
        {
            if (AppState.Current.CurrentOutletId <= 0 || AppState.Current.CurrentCounterId <= 0)
                throw new InvalidOperationException("This PC is not assigned to a counter. Please contact Admin.");
        }
    }
}
