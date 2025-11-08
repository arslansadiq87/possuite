// Pos.Client.Wpf/Services/Sync/SyncHttp.cs
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pos.Domain.Sync;
using System.Linq;

namespace Pos.Client.Wpf.Services.Sync;

public interface ISyncHttp
{
    Task<(int accepted, long serverToken)> PushAsync(SyncBatch batch, CancellationToken ct);
    Task<(List<SyncEnvelope> changes, long serverToken)> PullAsync(string terminalId, long sinceToken, int max, CancellationToken ct);
    Task<bool> PingAsync(CancellationToken ct = default);
}

public sealed class SyncHttp : ISyncHttp
{
    private readonly HttpClient _http;
    private readonly ILogger<SyncHttp>? _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SyncHttp(HttpClient http, ILogger<SyncHttp>? log = null)
    {
        _http = http;
        _log = log;
    }

    public async Task<(int accepted, long serverToken)> PushAsync(SyncBatch batch, CancellationToken ct)
    {
        // FromToken is non-nullable long (init-only) — just read it
        long fromToken = batch.FromToken;

        var changes = (batch.Changes ?? new List<SyncEnvelope>())
            .Select(c =>
            {
                var ts = c.TsUtc.Kind == DateTimeKind.Utc
                    ? c.TsUtc
                    : DateTime.SpecifyKind(c.TsUtc, DateTimeKind.Utc);

                return new ChangeDto(
                    c.Entity,
                    c.PublicId,
                    (int)c.Op,
                    c.PayloadJson,
                    ts
                );
            })
            .ToList();

        var dto = new BatchDto(batch.TerminalId, fromToken, changes);

        var json = JsonSerializer.Serialize(dto, JsonOpts);
        _log?.LogInformation("SYNC PUSH → {Json}", json);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync("api/sync/push", content, ct);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _log?.LogError("SYNC PUSH failed: {Code} {Reason}. Body: {Body}",
                (int)res.StatusCode, res.ReasonPhrase, body);
            throw new HttpRequestException(
                $"Push failed {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
        }

        var obj = await res.Content.ReadFromJsonAsync<PushResp>(JsonOpts, ct);
        if (obj is null) throw new InvalidOperationException("Push returned empty response.");

        _log?.LogInformation("SYNC PUSH ✓ Accepted={Accepted} ServerToken={Token}", obj.Accepted, obj.ServerToken);
        return (obj.Accepted, obj.ServerToken);
    }

    public async Task<(List<SyncEnvelope> changes, long serverToken)> PullAsync(
        string terminalId, long sinceToken, int max, CancellationToken ct)
    {
        // Server expects 'since'
        var url = $"api/sync/pull?terminalId={Uri.EscapeDataString(terminalId)}&since={sinceToken}&max={max}";
        _log?.LogInformation("SYNC PULL → {Url}", url);

        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _log?.LogError("SYNC PULL failed: {Code} {Reason}. Body: {Body}",
                (int)res.StatusCode, res.ReasonPhrase, body);
            throw new HttpRequestException(
                $"Pull failed {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
        }

        var obj = await res.Content.ReadFromJsonAsync<PullResp>(JsonOpts, ct);
        if (obj is null) throw new InvalidOperationException("Pull returned empty response.");

        _log?.LogInformation("SYNC PULL ✓ {Count} changes, serverToken={Token}", obj.Changes.Count, obj.ServerToken);
        return (obj.Changes, obj.ServerToken);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/health");
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    // ---- transport DTOs (client → server) ----
    private sealed record BatchDto(
        string TerminalId,
        long FromToken,
        List<ChangeDto> Changes
    );

    private sealed record ChangeDto(
        string Entity,
        Guid PublicId,
        int Op,
        string PayloadJson,
        DateTime TsUtc
    );

    // ---- response DTOs (server → client) ----
    private sealed record PushResp(int Accepted, long ServerToken);
    private sealed record PullResp(List<SyncEnvelope> Changes, long ServerToken);
}
