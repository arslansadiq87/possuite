using System;

namespace Pos.Client.Wpf.Infrastructure
{
    public static class AppEvents
    {
        public static event Action? AccountsChanged;

        public static void RaiseAccountsChanged() => AccountsChanged?.Invoke();
    }
}
