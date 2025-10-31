using Pos.Domain;

namespace Pos.Client.Wpf.Services
{
    public interface ITerminalContext
    {
        int OutletId { get; }
        int CounterId { get; }
    }

    public sealed class TerminalContext : ITerminalContext
    {
        public int OutletId => AppState.Current?.CurrentOutletId ?? 1;
        public int CounterId => AppState.Current?.CurrentCounterId ?? 1;
    }
}
