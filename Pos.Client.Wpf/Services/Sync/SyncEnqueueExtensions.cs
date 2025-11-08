using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Abstractions;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Client.Wpf.Services.Sync;

public static class SyncEnqueueExtensions
{
    /// <summary>
    /// Call this immediately after your existing SaveChangesAsync() when you've just inserted/updated an entity.
    /// It enqueues to Outbox and persists the Outbox row.
    /// </summary>
    public static async Task EnqueueAfterSaveAsync<T>(
        this IOutboxWriter outbox,
        PosClientDbContext db,
        T entity,
        CancellationToken ct = default) where T : BaseEntity
    {
        // You already called db.SaveChangesAsync() for the entity before this helper.
        await outbox.EnqueueUpsertAsync(db, entity, ct);
        await db.SaveChangesAsync(ct); // persist the Outbox row
    }
}
