using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Sync;
using Pos.Server.Api;
using Npgsql; // <-- needed for NpgsqlConnectionStringBuilder if you use it

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------
// EF Core / PostgreSQL
// ----------------------------------------------------------------------
// appsettings.json -> "ConnectionStrings": { "ServerDb": "Host=localhost;Port=5432;Database=posserver;Username=posapp;Password=Strong#Pass1;Pooling=true" }

builder.Services.AddDbContext<ServerDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("ServerDb")
         ?? Environment.GetEnvironmentVariable("DT_PG_CS");

    opt.UseNpgsql(cs, npg =>
    {
        // optional: map timestamp behavior if you need legacy semantics
        // AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        // retries on transient network hiccups
        npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
    });

    // optional: better query exceptions during dev
    opt.EnableDetailedErrors();
    opt.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
});

// ----------------------------------------------------------------------
// Swagger / CORS
// ----------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.WebHost.UseUrls("http://localhost:5089");
var app = builder.Build();

// ----------------------------------------------------------------------
// Apply migrations on boot (server-side only)
// ----------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
app.UseSwagger();
app.UseSwaggerUI();

// Simple health check
app.MapGet("/api/health", () => Results.Ok(new { ok = true, utc = DateTime.UtcNow }));

// -----------------------------
// Push: client -> server (append to feed)
// -----------------------------
// -----------------------------
// Push: client -> server (append to change feed)
// -----------------------------
app.MapPost("/api/sync/push", async (
    ServerDbContext db,
    ILoggerFactory lf,
    [FromBody] SyncBatch batch,
    CancellationToken ct) =>
{
    var log = lf.CreateLogger("SyncPush");

    if (batch is null || batch.Changes is null || batch.Changes.Count == 0)
        return Results.BadRequest(new { error = "Empty batch." });

    try
    {
        // When EnableRetryOnFailure is on, manual tx must run under the execution strategy
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<object?>(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // compute next token (0 if none)
            var last = await db.Changes
                .OrderByDescending(c => c.Token)
                .Select(c => c.Token)
                .FirstOrDefaultAsync(ct);

            foreach (var ch in batch.Changes)
            {
                last = checked(last + 1);
                db.Changes.Add(new ServerChange
                {
                    Token = last,
                    Entity = ch.Entity,
                    PublicId = ch.PublicId,
                    Op = (int)ch.Op,
                    PayloadJson = ch.PayloadJson,
                    TsUtc = ch.TsUtc,
                    SourceTerminal = batch.TerminalId
                });
            }

            // upsert cursor
            var cursor = await db.Cursors.SingleOrDefaultAsync(c => c.TerminalId == batch.TerminalId, ct);
            if (cursor is null)
                db.Cursors.Add(new ServerCursor { TerminalId = batch.TerminalId, LastToken = last });
            else
                cursor.LastToken = last;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            log.LogInformation("Pushed {Count} changes from {Terminal} -> token {Token}", batch.Changes.Count, batch.TerminalId, last);
            return Results.Ok(new { Accepted = batch.Changes.Count, ServerToken = last });
        })!;
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Push failed for terminal {Terminal}", batch.TerminalId);
        return Results.Problem(
            title: "Push failed",
            detail: ex.GetBaseException().Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

// -----------------------------
// Pull: server -> client (deltas since token)
// Supports: /api/sync/pull?terminalId=KHI-OUT-01&since=123&max=500
// -----------------------------
app.MapGet("/api/sync/pull", async (
    [FromServices] ServerDbContext db,
    [FromQuery] string terminalId,
    [FromQuery(Name = "since")] long sinceToken,
    [FromQuery] int max = 500,
    CancellationToken ct = default) =>
{
    if (max <= 0 || max > 5000) max = 500;

    var changes = await db.Changes
        .Where(c => c.Token > sinceToken && c.SourceTerminal != terminalId)
        .OrderBy(c => c.Token)
        .Take(max)
        .Select(c => new SyncEnvelope
        {
            Entity = c.Entity,
            Op = (SyncOp)c.Op,
            PublicId = c.PublicId,
            PayloadJson = c.PayloadJson,
            TsUtc = c.TsUtc
        })
        .ToListAsync(ct);

    var lastToken = await db.Changes
        .OrderByDescending(c => c.Token)
        .Select(c => c.Token)
        .FirstOrDefaultAsync(ct);

    return Results.Ok(new { Changes = changes, ServerToken = lastToken });
});

app.Run();
