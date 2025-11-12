using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Sync
{
    /// <summary>
    /// Standardizes Upsert/Delete operations for the outbox across services,
    /// without requiring changes to IOutboxWriter. It will try to invoke an
    /// existing method on your writer (e.g., EnqueueAsync/WriteAsync/AppendAsync)
    /// using common signatures.
    /// </summary>
    public static class OutboxWriterExtensions
    {
        public static Task EnqueueUpsertAsync(this IOutboxWriter outbox,
            string table, object key, object payload, CancellationToken ct = default)
            => InvokeKnown(outbox, table, "upsert", key, payload, ct);

        public static Task EnqueueDeleteAsync(this IOutboxWriter outbox,
            string table, object key, CancellationToken ct = default)
            => InvokeKnown(outbox, table, "delete", key, payload: null, ct);

        // ---- internals ----

        private static Task InvokeKnown(IOutboxWriter outbox,
            string table, string op, object key, object? payload, CancellationToken ct)
        {
            var json = payload is null ? null : JsonSerializer.Serialize(payload);

            // Try common async method names in typical order
            return TryInvoke(outbox, "EnqueueAsync", table, op, key, json, ct)
                ?? TryInvoke(outbox, "WriteAsync", table, op, key, json, ct)
                ?? TryInvoke(outbox, "AppendAsync", table, op, key, json, ct)
                ?? TryInvokeUpsertDeleteDirect(outbox, op, table, key, payload, ct)
                ?? throw new NotSupportedException(
                    "IOutboxWriter has no compatible async method (EnqueueAsync/WriteAsync/AppendAsync/UpsertAsync/DeleteAsync). " +
                    "Add one, or update OutboxWriterExtensions to match your implementation.");
        }

        // Signature candidates: (string table, string op, object key, string? json, CancellationToken ct)
        private static Task? TryInvoke(IOutboxWriter outbox, string methodName,
            string table, string op, object key, string? json, CancellationToken ct)
        {
            var m = outbox.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return null;

            var ps = m.GetParameters();
            try
            {
                // Try (string, string, object, string?, CancellationToken)
                if (ps.Length == 5 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(string) &&
                    ps[2].ParameterType == typeof(object) &&
                    ps[3].ParameterType == typeof(string) &&
                    ps[4].ParameterType == typeof(CancellationToken))
                {
                    var task = (Task?)m.Invoke(outbox, new object?[] { table, op, key, json!, ct });
                    return task!;
                }

                // Try (string, string, string, string?, CancellationToken) — if key is string
                if (ps.Length == 5 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(string) &&
                    ps[2].ParameterType == typeof(string) &&
                    ps[3].ParameterType == typeof(string) &&
                    ps[4].ParameterType == typeof(CancellationToken))
                {
                    var keyStr = key?.ToString() ?? "";
                    var task = (Task?)m.Invoke(outbox, new object?[] { table, op, keyStr, json!, ct });
                    return task!;
                }

                // Try (string, string, string?, CancellationToken) — if your writer infers key in payload
                if (ps.Length == 4 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(string) &&
                    ps[2].ParameterType == typeof(string) &&
                    ps[3].ParameterType == typeof(CancellationToken))
                {
                    var task = (Task?)m.Invoke(outbox, new object?[] { table, op, json!, ct });
                    return task!;
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }

            return null;
        }

        // Direct UpsertAsync/DeleteAsync if your implementation actually has them (different signature)
        private static Task? TryInvokeUpsertDeleteDirect(IOutboxWriter outbox, string op,
            string table, object key, object? payload, CancellationToken ct)
        {
            try
            {
                var m = outbox.GetType().GetMethod(
                    op.Equals("upsert", StringComparison.OrdinalIgnoreCase) ? "UpsertAsync" : "DeleteAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m == null) return null;

                var ps = m.GetParameters();

                // UpsertAsync(table, key, payload, ct)
                if (op == "upsert" && ps.Length == 4 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(object) &&
                    ps[2].ParameterType == typeof(object) &&
                    ps[3].ParameterType == typeof(CancellationToken))
                {
                    return (Task?)m.Invoke(outbox, new object?[] { table, key, payload!, ct });
                }

                // DeleteAsync(table, key, ct)
                if (op == "delete" && ps.Length == 3 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(object) &&
                    ps[2].ParameterType == typeof(CancellationToken))
                {
                    return (Task?)m.Invoke(outbox, new object?[] { table, key, ct });
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }

            return null;
        }
    }
}
