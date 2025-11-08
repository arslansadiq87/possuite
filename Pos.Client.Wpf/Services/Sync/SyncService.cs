// Pos.Client.Wpf/Services/Sync/SyncService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Sync;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Client.Wpf.Services.Sync;

public interface ISyncService
{
    Task PushAsync(CancellationToken ct = default);
    Task PullAsync(CancellationToken ct = default);
}

public sealed class SyncService : ISyncService
{
    private readonly PosClientDbContext _db;
    private readonly ISyncHttp _http;

    public string TerminalId { get; } // e.g., OutletCode + CounterCode
    public SyncService(PosClientDbContext db, ISyncHttp http)
    {
        _db = db; _http = http;
        // You likely already have outlet/counter bindings; reuse them:
        TerminalId = "OUTLET-01/COUNTER-01"; // TODO: get from your binding service
    }

    public async Task PushAsync(CancellationToken ct = default)
    {
        var lastToken = await _db.SyncCursors.Select(c => c.LastToken).FirstOrDefaultAsync(ct);
        var pending = await _db.SyncOutbox.OrderBy(x => x.Token).Take(500).ToListAsync(ct);
        if (pending.Count == 0) return;

        var batch = new SyncBatch
        {
            TerminalId = TerminalId,
            FromToken = lastToken,
            Changes = pending.Select(p => new SyncEnvelope
            {
                Entity = p.Entity,
                Op = (SyncOp)p.Op,
                PublicId = p.PublicId,
                PayloadJson = p.PayloadJson,
                TsUtc = p.TsUtc
            }).ToList()
        };

        var (accepted, serverToken) = await _http.PushAsync(batch, ct);

        // On success, remove pushed rows (idempotency: only remove those we sent)
        _db.SyncOutbox.RemoveRange(pending);
        await _db.SaveChangesAsync(ct);

        await EnsureCursorAsync(serverToken, ct);
    }

    public async Task PullAsync(CancellationToken ct = default)
    {
        var cursor = await _db.SyncCursors.FirstOrDefaultAsync(ct) ?? new SyncCursor { LastToken = 0 };
        if (cursor.Id == 0) _db.SyncCursors.Add(cursor);

        var (changes, serverToken) = await _http.PullAsync(TerminalId, cursor.LastToken, 500, ct);
        if (changes.Count == 0)
        {
            await EnsureCursorAsync(serverToken, ct);
            return;
        }

        // 1) Write to local inbox (audit/trace)
        long localToken = cursor.LastToken;
        foreach (var ch in changes)
        {
            localToken = checked(localToken + 1);
            _db.SyncInbox.Add(new SyncInbox
            {
                Entity = ch.Entity,
                PublicId = ch.PublicId,
                Op = (int)ch.Op,
                PayloadJson = ch.PayloadJson,
                TsUtc = ch.TsUtc,
                Token = localToken
            });
            // 2) Apply to local tables (simple LWW for master, append-only for docs)
            await ApplyAsync(ch, ct);
        }

        cursor.LastToken = serverToken; // move to server watermark
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureCursorAsync(long serverToken, CancellationToken ct)
    {
        var cursor = await _db.SyncCursors.FirstOrDefaultAsync(ct) ?? new SyncCursor { LastToken = 0 };
        if (cursor.Id == 0) _db.SyncCursors.Add(cursor);
        cursor.LastToken = serverToken;
        await _db.SaveChangesAsync(ct);
    }

    private Task ApplyAsync(SyncEnvelope ch, CancellationToken ct)
    {
        // IMPORTANT: keep it simple first.
        // - For immutable posted docs (Sale, Voucher, Purchase): Upsert by PublicId. No delete.
        // - For master data (Item, Price, Customer, Supplier): last-writer-wins based on ch.TsUtc.
        // You can deserialize ch.PayloadJson into your entity type by name via reflection or a map.

        // Pseudocode (fill out with your real types):
        // var type = Type.GetType("Pos.Domain.Entities." + ch.Entity + ", Pos.Domain");
        // var entity = (BaseEntity)JsonSerializer.Deserialize(ch.PayloadJson, type)!;
        // _db.Update(entity); // EF upsert-ish (or track by PublicId via natural key)
        // return Task.CompletedTask;

        return Task.CompletedTask;
    }
}
