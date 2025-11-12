namespace Pos.Domain.Services
{
    /// <summary>
    /// UI-provided current terminal scope (ids only). No EF here.
    /// </summary>
    public interface ITerminalContext
    {
        int OutletId { get; }
        int CounterId { get; }
    }
}
