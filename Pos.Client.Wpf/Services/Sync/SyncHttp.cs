// Pos.Client.Wpf/Services/Sync/SyncHttp.cs
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pos.Domain.Sync;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Services.Sync;

public interface ISyncHttp
{
    Task<(int accepted, long serverToken)> PushAsync(SyncBatch batch, CancellationToken ct);
    Task<(List<SyncEnvelope> changes, long serverToken)> PullAsync(string terminalId, long sinceToken, int max, CancellationToken ct);
}

public sealed class SyncHttp : ISyncHttp
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ILogger<SyncHttp> _log;
    private readonly IServerSettingsService _settings;


    public SyncHttp(HttpClient http, ILogger<SyncHttp> log, IServerSettingsService settings)
    {
        _http = http;
        _log = log;
        _settings = settings;
    }

    private async Task ConfigureAsync(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(s.BaseUrl)) throw new InvalidOperationException("Server BaseUrl not set.");
        _http.BaseAddress = new Uri(s.BaseUrl);
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        if (!string.IsNullOrWhiteSpace(s.ApiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", s.ApiKey);
    }

    public async Task<(int accepted, long serverToken)> PushAsync(SyncBatch batch, CancellationToken ct)
    {
        try
        {
            await ConfigureAsync(ct);
            using var resp = await _http.PostAsJsonAsync("/api/sync/push", batch, JsonOpts, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<PushResp>(JsonOpts, ct)
                       ?? new PushResp(0, batch.FromToken);

            return (body.Accepted, body.ServerToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[SYNC] Push failed");
            return (0, batch.FromToken);
        }
    }

    public async Task<(List<SyncEnvelope> changes, long serverToken)> PullAsync(string terminalId, long sinceToken, int max, CancellationToken ct)
    {
        try
        {
            await ConfigureAsync(ct);
            var uri = $"/api/sync/pull?terminalId={Uri.EscapeDataString(terminalId)}&sinceToken={sinceToken}&max={max}";
            using var resp = await _http.GetAsync(uri, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<PullResp>(JsonOpts, ct)
                       ?? new PullResp(new List<SyncEnvelope>(), sinceToken);

            return (body.Changes ?? new List<SyncEnvelope>(), body.ServerToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[SYNC] Pull failed");
            return (new List<SyncEnvelope>(), sinceToken);
        }
    }

    // ---- response DTOs (server → client) ----
    private sealed record PushResp(int Accepted, long ServerToken);
    private sealed record PullResp(List<SyncEnvelope> Changes, long ServerToken);
}
