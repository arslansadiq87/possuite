// Pos.Persistence/Sync/OutboxWriter.cs
using System.Text.Json;
using Pos.Domain.Abstractions;
using Pos.Domain.Sync;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Pos.Persistence.Sync;

public interface IOutboxWriter
{
    Task EnqueueUpsertAsync(PosClientDbContext db, object entity, CancellationToken ct = default);
    Task EnqueueDeleteAsync(PosClientDbContext db, string entityName, Guid publicId, CancellationToken ct = default);
}

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly ISyncTokenService _tokens;
    public OutboxWriter(ISyncTokenService tokens) => _tokens = tokens;

    // inside class OutboxWriter
    private static readonly JsonSerializerOptions _syncJson = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles, // prevents Item<->Barcodes loops
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        MaxDepth = 16 // keep payloads sane
    };


    public async Task EnqueueUpsertAsync(PosClientDbContext db, object entity, CancellationToken ct = default)
    {
        var t = entity.GetType();
        var name = t.Name; // e.g., "Sale", "Purchase", "Voucher"
        var publicId = ((BaseEntity)entity).PublicId;
        var payload = JsonSerializer.Serialize(entity, t, _syncJson);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var token = await _tokens.NextTokenAsync(db, ct);
                db.SyncOutbox.Add(new SyncOutbox
                {
                    Entity = name,
                    PublicId = publicId,
                    Op = (int)SyncOp.Upsert,
                    PayloadJson = payload,
                    TsUtc = DateTime.UtcNow,
                    Token = token
                });
                return; // success, exit loop
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
            {
                // regenerate and retry
                await Task.Delay(10, ct); // small delay helps in rapid concurrent inserts
            }
        }

        throw new InvalidOperationException("Outbox token generation failed after 3 retries.");
    }


    public async Task EnqueueDeleteAsync(PosClientDbContext db, string entityName, Guid publicId, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var token = await _tokens.NextTokenAsync(db, ct);
                db.SyncOutbox.Add(new SyncOutbox
                {
                    Entity = entityName,
                    PublicId = publicId,
                    Op = (int)SyncOp.Delete,
                    PayloadJson = "{}",
                    TsUtc = DateTime.UtcNow,
                    Token = token
                });
                return; // success
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
            {
                await Task.Delay(10, ct);
            }
        }

        throw new InvalidOperationException("Outbox token generation failed after 3 retries.");
    }

}
