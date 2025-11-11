// Pos.Persistence/Outbox/OutboxWriterExtensions.cs
using System.Threading;
using System.Threading.Tasks;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Outbox
{
    /// <summary>
    /// Temporary shim so catalog image code compiles offline.
    /// Replace the bodies to map to your actual outbox method(s) when you turn sync on.
    /// </summary>
    internal static class OutboxWriterExtensions
    {
        // Legacy 4-arg version (kept for compatibility)
        public static Task WriteAsync(this IOutboxWriter outbox,
                                      string entity,
                                      int rowId,
                                      string action,
                                      object payload)
            => Task.CompletedTask;

        // New 5-arg overload with CancellationToken
        public static Task WriteAsync(this IOutboxWriter outbox,
                                      string entity,
                                      int rowId,
                                      string action,
                                      object payload,
                                      CancellationToken ct)
            => Task.CompletedTask;
    }
}
