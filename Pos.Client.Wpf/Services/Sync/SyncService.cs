// Pos.Client.Wpf/Services/Sync/SyncService.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Abstractions;
using Pos.Domain.Sync;
using Pos.Domain.Services.System;
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
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDbContextFactory<PosClientDbContext> _dbFactory;
    private readonly ISyncHttp _http;
    private readonly IMachineIdentityService _machine;

    public SyncService(
        IDbContextFactory<PosClientDbContext> dbFactory,
        ISyncHttp http,
        IMachineIdentityService machine)
    {
        _dbFactory = dbFactory;
        _http = http;
        _machine = machine;
    }

    // ---------------- PUSH: local → server ----------------
    public async Task PushAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // single cursor row for server watermark
        var cursor = await db.SyncCursors
            .SingleOrDefaultAsync(c => c.Name == "server", ct);

        if (cursor == null)
        {
            cursor = new SyncCursor { Name = "server", LastToken = 0 };
            db.SyncCursors.Add(cursor);
            await db.SaveChangesAsync(ct);
        }

        // pending outbox rows ordered by local token
        const int maxBatchSize = 500;
        var pending = await db.SyncOutbox
            .OrderBy(o => o.Token)
            .Take(maxBatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        var terminalId = await _machine.GetMachineIdAsync(ct);

        var batch = new SyncBatch
        {
            TerminalId = terminalId,
            FromToken = cursor.LastToken,
            Changes = new List<SyncEnvelope>(pending.Count)
        };

        foreach (var o in pending)
        {
            batch.Changes.Add(new SyncEnvelope
            {
                Entity = o.Entity,
                Op = (SyncOp)o.Op,
                PublicId = o.PublicId,
                PayloadJson = o.PayloadJson,
                TsUtc = o.TsUtc
            });
        }

        var (accepted, serverToken) = await _http.PushAsync(batch, ct);

        if (accepted > 0)
        {
            // delete successfully pushed rows from outbox
            var pushedTokens = pending
                .OrderBy(o => o.Token)
                .Take(accepted)
                .Select(o => o.Token)
                .ToList();

            var toRemove = await db.SyncOutbox
                .Where(o => pushedTokens.Contains(o.Token))
                .ToListAsync(ct);

            db.SyncOutbox.RemoveRange(toRemove);
        }

        if (serverToken > cursor.LastToken)
        {
            cursor.LastToken = serverToken;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------- PULL: server → local ----------------
    public async Task PullAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var cursor = await db.SyncCursors
            .SingleOrDefaultAsync(c => c.Name == "server", ct);

        if (cursor == null)
        {
            cursor = new SyncCursor { Name = "server", LastToken = 0 };
            db.SyncCursors.Add(cursor);
            await db.SaveChangesAsync(ct);
        }

        var terminalId = await _machine.GetMachineIdAsync(ct);

        const int maxBatchSize = 500;
        var (changes, serverToken) = await _http.PullAsync(terminalId, cursor.LastToken, maxBatchSize, ct);

        if (changes.Count == 0)
        {
            // still move cursor to latest server watermark
            if (serverToken > cursor.LastToken)
            {
                cursor.LastToken = serverToken;
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        long localToken = cursor.LastToken;

        foreach (var ch in changes)
        {
            localToken = checked(localToken + 1);

            // 1) write into local inbox (audit)
            db.SyncInbox.Add(new SyncInbox
            {
                Entity = ch.Entity,
                PublicId = ch.PublicId,
                Op = (int)ch.Op,
                PayloadJson = ch.PayloadJson,
                TsUtc = ch.TsUtc,
                Token = localToken
            });

            // 2) apply to real tables
            await ApplyAsync(db, ch, ct);
        }

        cursor.LastToken = serverToken;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- APPLY: envelope → local entities ----------------
    // IMPORTANT:
    // - Posted docs (Sale, Purchase, Voucher, StockDoc): upsert by PublicId (append-only).
    // - Master data (Item, Product, Party, Warehouse, Outlet, InvoiceSettings*, IdentitySettings, ReceiptTemplate, etc.):
    //   last-writer-wins by PublicId; we rely on server as source of truth.

    private static readonly string[] EntityNamespaces =
    {
        "Pos.Domain.Entities",
        "Pos.Domain.Settings",
        "Pos.Domain.Accounting",
        "Pos.Domain.Hr"
    };

    private static async Task ApplyAsync(PosClientDbContext db, SyncEnvelope ch, CancellationToken ct)
    {
        // Resolve CLR type from entity name + known namespaces
        Type? clrType = null;
        foreach (var ns in EntityNamespaces)
        {
            clrType = Type.GetType($"{ns}.{ch.Entity}, Pos.Domain");
            if (clrType != null) break;
        }

        if (clrType == null)
        {
            // Unknown entity type – safely ignore
            return;
        }

        var entityObj = JsonSerializer.Deserialize(ch.PayloadJson, clrType, JsonOpts);
        if (entityObj is not BaseEntity baseEntity)
            return;

        if (ch.Op == SyncOp.Delete)
        {
            await DeleteByPublicIdAsync(db, baseEntity, ct);
        }
        else
        {
            await UpsertByPublicIdAsync(db, baseEntity, ct);
        }
    }

    // Generic upsert by PublicId using reflection → Set<TEntity>()
    private static Task UpsertByPublicIdAsync(PosClientDbContext db, BaseEntity entity, CancellationToken ct)
    {
        var method = typeof(SyncService)
            .GetMethod(nameof(UpsertGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

        var generic = method.MakeGenericMethod(entity.GetType());
        return (Task)generic.Invoke(null, new object[] { db, entity, ct })!;
    }

    private static async Task UpsertGeneric<TEntity>(PosClientDbContext db, BaseEntity entityBase, CancellationToken ct)
        where TEntity : BaseEntity
    {
        var entity = (TEntity)entityBase;

        var existing = await db.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.PublicId == entity.PublicId, ct);

        if (existing == null)
        {
            db.Set<TEntity>().Add(entity);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(entity);
        }
    }

    // Generic hard delete by PublicId (only used if you ever emit SyncOp.Delete)
    private static Task DeleteByPublicIdAsync(PosClientDbContext db, BaseEntity entity, CancellationToken ct)
    {
        var method = typeof(SyncService)
            .GetMethod(nameof(DeleteGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

        var generic = method.MakeGenericMethod(entity.GetType());
        return (Task)generic.Invoke(null, new object[] { db, entity, ct })!;
    }

    private static async Task DeleteGeneric<TEntity>(PosClientDbContext db, BaseEntity entityBase, CancellationToken ct)
        where TEntity : BaseEntity
    {
        var entity = (TEntity)entityBase;

        var existing = await db.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.PublicId == entity.PublicId, ct);

        if (existing != null)
        {
            db.Set<TEntity>().Remove(existing);
        }
    }
}
