using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Persistence.Sync
{
    /// <summary>
    /// Backward-compatible shims for IOutboxWriter.
    /// Prefers the new API (PosClientDbContext + entity), but will
    /// transparently fall back to older method names if present.
    /// </summary>
    public static class OutboxWriterExtensions
    {
        // === New preferred overloads ===
        public static Task EnqueueUpsertAsync(
            this IOutboxWriter outbox,
            PosClientDbContext db,
            object entity,
            CancellationToken ct = default)
        {
            if (outbox == null) throw new ArgumentNullException(nameof(outbox));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            // Fast path: new interface exists (compile-time)
            try
            {
                return outbox.EnqueueUpsertAsync(db, entity, ct);
            }
            catch (MissingMethodException)
            {
                // fall through to legacy
            }
            catch (NotImplementedException)
            {
                // fall through to legacy
            }

            // Legacy path: try older writer methods by reflection
            var (name, id) = ExtractEntityInfo(entity);
            return InvokeLegacyUpsert(outbox, name, id, entity, ct);
        }

        public static Task EnqueueDeleteAsync(
            this IOutboxWriter outbox,
            PosClientDbContext db,
            string entityName,
            Guid publicId,
            CancellationToken ct = default)
        {
            if (outbox == null) throw new ArgumentNullException(nameof(outbox));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name is required.", nameof(entityName));

            // Fast path: new interface exists (compile-time)
            try
            {
                return outbox.EnqueueDeleteAsync(db, entityName, publicId, ct);
            }
            catch (MissingMethodException)
            {
                // fall through to legacy
            }
            catch (NotImplementedException)
            {
                // fall through to legacy
            }

            // Legacy path
            return InvokeLegacyDelete(outbox, entityName, publicId, ct);
        }

        // === Optional legacy-shaped helpers (no db param) ===
        // If some old call sites are still calling these extension names,
        // keep them delegating to the new preferred overloads.
        public static Task UpsertAsync(this IOutboxWriter outbox, PosClientDbContext db, object entity, CancellationToken ct = default) =>
            EnqueueUpsertAsync(outbox, db, entity, ct);

        public static Task DeleteAsync(this IOutboxWriter outbox, PosClientDbContext db, string entityName, Guid publicId, CancellationToken ct = default) =>
            EnqueueDeleteAsync(outbox, db, entityName, publicId, ct);

        // ===== Internals =====

        private static (string entityName, Guid publicId) ExtractEntityInfo(object entity)
        {
            var type = entity.GetType();
            var name = type.Name;

            // Try PublicId (Guid) property
            var pidProp = type.GetProperty("PublicId", BindingFlags.Public | BindingFlags.Instance);
            if (pidProp != null && pidProp.PropertyType == typeof(Guid))
            {
                var val = (Guid)(pidProp.GetValue(entity) ?? Guid.Empty);
                if (val != Guid.Empty) return (name, val);
            }

            // Try Id (Guid) as a fallback
            var idProp = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (idProp != null && idProp.PropertyType == typeof(Guid))
            {
                var val = (Guid)(idProp.GetValue(entity) ?? Guid.Empty);
                if (val != Guid.Empty) return (name, val);
            }

            throw new InvalidOperationException($"Could not determine PublicId/Id for entity type {name}.");
        }

        private static Task InvokeLegacyUpsert(IOutboxWriter outbox, string entityName, Guid publicId, object payload, CancellationToken ct)
        {
            var t = outbox.GetType();

            // Try common legacy method names/signatures in order
            var candidates = new[]
            {
                // UpsertAsync(string, Guid, object, CancellationToken)
                ("UpsertAsync", new Type[] { typeof(string), typeof(Guid), typeof(object), typeof(CancellationToken) }),
                // WriteAsync(string, Guid, object, CancellationToken)
                ("WriteAsync",  new Type[] { typeof(string), typeof(Guid), typeof(object), typeof(CancellationToken) }),
                // AppendAsync(string, Guid, object, CancellationToken)
                ("AppendAsync", new Type[] { typeof(string), typeof(Guid), typeof(object), typeof(CancellationToken) }),
                // EnqueueAsync(string, Guid, object, CancellationToken)
                ("EnqueueAsync",new Type[] { typeof(string), typeof(Guid), typeof(object), typeof(CancellationToken) }),
            };

            foreach (var (name, sig) in candidates)
            {
                var mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, sig);
                if (mi != null)
                {
                    var task = (Task)mi.Invoke(outbox, new object[] { entityName, publicId, payload, ct })!;
                    return task;
                }
            }

            throw new MissingMethodException("IOutboxWriter has no compatible legacy upsert method (EnqueueAsync/WriteAsync/AppendAsync/UpsertAsync).");
        }

        private static Task InvokeLegacyDelete(IOutboxWriter outbox, string entityName, Guid publicId, CancellationToken ct)
        {
            var t = outbox.GetType();

            var candidates = new[]
            {
                // DeleteAsync(string, Guid, CancellationToken)
                ("DeleteAsync", new Type[] { typeof(string), typeof(Guid), typeof(CancellationToken) }),
                // RemoveAsync(string, Guid, CancellationToken)
                ("RemoveAsync", new Type[] { typeof(string), typeof(Guid), typeof(CancellationToken) }),
            };

            foreach (var (name, sig) in candidates)
            {
                var mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, sig);
                if (mi != null)
                {
                    var task = (Task)mi.Invoke(outbox, new object[] { entityName, publicId, ct })!;
                    return task;
                }
            }

            throw new MissingMethodException("IOutboxWriter has no compatible legacy delete method (DeleteAsync/RemoveAsync).");
        }

        private static MethodInfo? GetMethod(this Type t, string name, BindingFlags flags, Type[] signature)
        {
            return t.GetMethods(flags).FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.Ordinal) &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature));
        }

        // OutboxWriterExtensions.cs  (add these near the other public methods)
        public static Task EnqueueUpsertAsync(
            this IOutboxWriter outbox,
            Pos.Persistence.PosClientDbContext db,
            string entityName,
            int intId,
            object payload,
            CancellationToken ct = default)
        {
            if (outbox == null) throw new ArgumentNullException(nameof(outbox));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name is required.", nameof(entityName));

            // Create a stable pseudo-Guid from the integer id (namespaced)
            var publicId = CreateDeterministicGuid(entityName, intId);

            // Wrap as a lightweight envelope so new writer can accept it
            var envelope = new LegacyEnvelope(entityName, publicId, payload);
            return outbox.EnqueueUpsertAsync(db, envelope, ct);
        }

        public static Task EnqueueDeleteAsync(
            this IOutboxWriter outbox,
            Pos.Persistence.PosClientDbContext db,
            string entityName,
            int intId,
            CancellationToken ct = default)
        {
            if (outbox == null) throw new ArgumentNullException(nameof(outbox));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name is required.", nameof(entityName));

            var publicId = CreateDeterministicGuid(entityName, intId);
            return outbox.EnqueueDeleteAsync(db, entityName, publicId, ct);
        }

        // --- helpers (put inside the same class) ---
        private static Guid CreateDeterministicGuid(string entityName, int intId)
        {
            // simple, stable v3-like GUID from "entityName:intId"
            var input = System.Text.Encoding.UTF8.GetBytes($"{entityName}:{intId}");
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(input);
            return new Guid(hash);
        }

        // A tiny envelope so the new writer (object entity) path works fine
        private sealed record LegacyEnvelope(string Entity, Guid PublicId, object Payload);

    }
}
