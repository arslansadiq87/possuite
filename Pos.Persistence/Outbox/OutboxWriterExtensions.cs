// Pos.Persistence/Outbox/OutboxWriterExtensions.cs
using System.Threading.Tasks;
using Pos.Domain.Abstractions;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Outbox
{
    /// <summary>
    /// Temporary shim so catalog image code compiles offline.
    /// Replace the body to map to your actual outbox method when you turn sync on.
    /// </summary>
    internal static class OutboxWriterExtensions
    {
        public static Task WriteAsync(this IOutboxWriter outbox,
                                      string entity,
                                      int rowId,
                                      string action,
                                      object payload) =>
            Task.CompletedTask;
    }
}
