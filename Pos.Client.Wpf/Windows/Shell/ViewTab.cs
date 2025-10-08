using System;

namespace Pos.Client.Wpf.Windows.Shell
{
    public sealed class ViewTab
    {
        public required string Title { get; init; }
        public required object Content { get; init; }   // a UserControl instance
        public bool CanClose { get; init; } = true;

        // Optional: help with contextual ribbon routing
        public string? ContextKey { get; init; }        // e.g., "Transfer", "Sales"
        public Guid Id { get; } = Guid.NewGuid();
    }
}
