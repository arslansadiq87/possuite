using Pos.Domain.Services;

namespace Pos.Client.Wpf.Services
{
    public sealed class TerminalContext : ITerminalContext
    {
        // Source of truth for ids remains the AppState (or wherever you store it)
        public int OutletId => AppState.Current?.CurrentOutletId ?? 1;
        public int CounterId => AppState.Current?.CurrentCounterId ?? 1;
    }
}
