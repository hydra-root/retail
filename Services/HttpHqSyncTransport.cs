using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-08: the in-memory mirror of the two facts the DI factory needs to pick a transport —
/// is the live endpoint configured, and why. Reading <see cref="SyncSetting"/> means a database
/// query, and the factory is synchronous, so the answer is cached here instead:
/// <see cref="SyncCenterService.PrimeTransportSelection"/> fills it at startup and the transport
/// refreshes it on every call, which means a settings change takes effect from the next run
/// without restarting the till.
/// It also makes the choice reportable: <see cref="Reason"/> says what WOULD run and why, which is
/// what the control centre shows. The selection never upgrades the till on its own — it only
/// reflects what the saved settings say.
/// </summary>
public sealed class HqTransportSelection
{
    private volatile bool _isLive;
    private volatile string _reason = "No HQ connection has been read yet, so the queue replays " +
                                      "through the built-in simulated endpoint.";

    public bool IsLive => _isLive;

    public string Reason => _reason;

    /// <summary>Re-evaluate the saved settings. A null row means "never configured" → simulated.</summary>
    public void Refresh(SyncSetting? settings)
    {
        if (settings == null)
        {
            _isLive = false;
            _reason = "The sync connection could not be read from the local database, so the queue " +
                      "replays through the simulated endpoint.";
            return;
        }

        if (!settings.IsEnabled)
        {
            _isLive = false;
            _reason = "Live HQ sync is switched OFF in Connection settings, so the simulated endpoint " +
                      "replays the queue and no server is contacted.";
            return;
        }

        if (!SyncSetting.TryValidateBaseUrl(settings.BaseUrl, settings.UseTlsOnly, out var error))
        {
            _isLive = false;
            _reason = $"Live HQ sync is switched ON but cannot be used yet — {error} " +
                      "Until it is saved with a usable address, runs keep using the simulated endpoint.";
            return;
        }

        _isLive = true;
        _reason = $"Live HQ sync is ON, so runs are POSTed to {SyncSetting.HostLabel(settings.BaseUrl)} " +
                  $"as device '{(string.IsNullOrWhiteSpace(settings.DeviceIdentifier) ? "unnamed till" : settings.DeviceIdentifier)}'. " +
                  "The simulated endpoint is no longer used.";
    }
}

/// <summary>
/// SEC-08: the real end of the queue — <see cref="IHqSyncTransport"/> over HTTPS, so a till that
/// can reach HQ hands its changes to HQ instead of to a stand-in.
/// Design notes that matter for the rest of the app:
/// <list type="bullet">
///   <item>It never holds a scoped <see cref="AppDbContext"/>. Configuration is read through its own
///   DI scope per call, which is what lets the container keep it as a single instance with one
///   shared <see cref="HttpClient"/> (no socket churn, no captive dependency, no cross-thread context).</item>
///   <item><see cref="ProbeAsync"/> is cheap (one GET on the health path) and its wait is capped at
///   <see cref="MaxProbeSeconds"/>, because the queue awaits it from the UI thread.</item>
///   <item>The API key is only ever placed in a request header. It is stripped from anything the
///   class reports back, so a server that echoes it in an error body still cannot put it in a log.</item>
/// </list>
/// </summary>
public class HttpHqSyncTransport : IHqSyncTransport
{
    /// <summary>Health path probed under the API root: GET {base}/health.</summary>
    public const string HealthSegment = "health";

    /// <summary>Batch path posted under the API root: POST {base}/changes.</summary>
    public const string ChangesSegment = "changes";

    /// <summary>Probe ceiling. The operator must never watch a frozen screen waiting for HQ.</summary>
    public const int MaxProbeSeconds = 5;

    private const int MaxSnippetChars = 220;

    /// <summary>
    /// One client for the whole process: HttpClient pools connections and is not meant to be
    /// created per request. The real wait limit is a per-request CancellationTokenSource, because
    /// Timeout is fixed on the first send and the operator may change it later.
    /// Redirects are refused so a mis-typed address cannot silently move the key to another host.
    /// </summary>
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HqTransportSelection _selection;

    /// <summary>Last configuration seen, used only for display (the name) — never the key.</summary>
    private volatile SyncSetting? _cached;

    public HttpHqSyncTransport(IServiceScopeFactory scopeFactory, HqTransportSelection selection)
    {
        _scopeFactory = scopeFactory;
        _selection = selection;
    }

    public string EndpointName => $"HQ endpoint (live) {SyncSetting.HostLabel(_cached?.BaseUrl)}";

    /// <summary>
    /// Verdict of the most recent probe in plain language, including the actionable part
    /// (bad key, wrong address, server down, too slow). Read by the control centre after
    /// Test connection; the queue itself only uses the boolean.
    /// </summary>
    public string LastProbeDetail { get; private set; } =
        "This till has not probed HQ yet — press Test connection to ask the endpoint.";

    /// <summary>Why the container handed the queue this transport instead of the simulated one.</summary>
    public string SelectionReason => _selection.Reason;

    // ---- IHqSyncTransport ----

    public async Task<bool> ProbeAsync()
    {
        var settings = await LoadSettingsAsync();

        if (settings == null)
        {
            LastProbeDetail = "The local database did not answer, so the connection settings could not be " +
                              "read and nothing was sent. Retry in a moment.";
            return false;
        }

        if (!settings.IsEnabled)
        {
            LastProbeDetail = "Live HQ sync is switched OFF, so this till did not contact the server. " +
                              "Tick the live sync switch and save to start sending to HQ.";
            return false;
        }

        if (!SyncSetting.TryValidateBaseUrl(settings.BaseUrl, settings.UseTlsOnly, out var urlError))
        {
            LastProbeDetail = urlError ?? "The HQ address is not usable.";
            return false;
        }

        if (!settings.HasApiKey)
        {
            LastProbeDetail = "No API key is stored, so HQ would refuse this till even if it answered. " +
                              "Enter the key HQ issued, save, and test again.";
            return false;
        }

        var wait = ProbeWait(settings.TimeoutSeconds);
        using var timer = new CancellationTokenSource(wait);
        try
        {
            using var request = BuildRequest(HttpMethod.Get, settings, HealthSegment, null);
            using var response = await Client.SendAsync(request, timer.Token);
            var snippet = await ReadSnippetAsync(response, settings);

            var healthy = response.IsSuccessStatusCode;
            LastProbeDetail = DescribeResponse(response, settings, healthy, snippet);
            return healthy;
        }
        catch (OperationCanceledException)
        {
            LastProbeDetail = $"{SyncSetting.HostLabel(settings.BaseUrl)} did not answer within " +
                              $"{wait.TotalSeconds:0} seconds. The server may be asleep or the network " +
                              "down — nothing was sent and no attempts were used.";
            return false;
        }
        catch (HttpRequestException ex)
        {
            LastProbeDetail = $"{SyncSetting.HostLabel(settings.BaseUrl)} could not be reached: " +
                              $"{Sanitize(ex.Message, settings.ApiKey)}. This is a network or address " +
                              "problem, not a queue problem — your changes are still waiting locally.";
            return false;
        }
        catch (Exception ex)
        {
            LastProbeDetail = $"Checking the HQ connection failed ({ex.GetType().Name}): " +
                              $"{Sanitize(ex.Message, settings.ApiKey)}";
            return false;
        }
    }

    public async Task<HqSyncResult> SendAsync(IReadOnlyList<HqSyncRow> rows)
    {
        if (rows.Count == 0)
            return new HqSyncResult(true, 0, null);

        var settings = await LoadSettingsAsync();

        if (settings == null)
            return new HqSyncResult(false, 0, "The local database did not answer, so the HQ connection " +
                                              "settings could not be read. Nothing was sent.");

        if (!settings.IsEnabled)
            return new HqSyncResult(false, 0, "Live HQ sync is switched OFF, so this till will not post " +
                                              "to a server. Use the switch on the Sync Centre screen.");

        if (!SyncSetting.TryValidateBaseUrl(settings.BaseUrl, settings.UseTlsOnly, out var urlError))
            return new HqSyncResult(false, 0, urlError ?? "The HQ address is not usable.");

        if (!settings.HasApiKey)
            return new HqSyncResult(false, 0, "No API key is stored, so HQ would refuse this till. " +
                                              "Enter the key HQ issued on the Sync Centre screen.");

        var batchSize = Math.Clamp(settings.BatchSize, 1, 500);
        var accepted = 0;

        // Ordered rows, prefix semantics: FlushAsync marks the first N rows delivered, so the run
        // stops at the first call HQ did not take and reports everything delivered before it.
        for (var start = 0; start < rows.Count; start += batchSize)
        {
            var chunk = rows.Skip(start).Take(batchSize).ToList();
            var (ok, count, error) = await SendChunkAsync(settings, chunk);

            if (!ok)
                return new HqSyncResult(false, accepted, error);

            accepted += count;
        }

        return new HqSyncResult(true, accepted, null);
    }

    // ---- one POST ----

    private async Task<(bool Ok, int Accepted, string? Error)> SendChunkAsync(SyncSetting settings,
        IReadOnlyList<HqSyncRow> chunk)
    {
        var wait = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 300));
        using var timer = new CancellationTokenSource(wait);

        try
        {
            var json = BuildBatchJson(settings, chunk);
            using var request = BuildRequest(HttpMethod.Post, settings, ChangesSegment,
                new StringContent(json, Encoding.UTF8, "application/json"));
            using var response = await Client.SendAsync(request, timer.Token);
            var snippet = await ReadSnippetAsync(response, settings);

            if (response.IsSuccessStatusCode)
            {
                var taken = ParseAcceptedCount(snippet, chunk.Count);
                return (true, Math.Clamp(taken, 0, chunk.Count), null);
            }

            return (false, 0, DescribeResponse(response, settings, false, snippet));
        }
        catch (OperationCanceledException)
        {
            return (false, 0, $"{SyncSetting.HostLabel(settings.BaseUrl)} did not take the batch within " +
                              $"{wait.TotalSeconds:0} seconds. HQ may be busy — retry once the queue " +
                              "screen shows the link back; each row used one attempt.");
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, $"{SyncSetting.HostLabel(settings.BaseUrl)} could not be reached while " +
                              $"sending {chunk.Count} change(s): {Sanitize(ex.Message, settings.ApiKey)} " +
                              "The changes stay in the local queue.");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Sending to HQ failed ({ex.GetType().Name}): " +
                              $"{Sanitize(ex.Message, settings.ApiKey)}");
        }
    }

    /// <summary>
    /// The document HQ receives: the till's identity plus the queue rows in replay order, with each
    /// row's payload embedded as JSON (not as an escaped string) so HQ can read it as a document.
    /// </summary>
    private static string BuildBatchJson(SyncSetting settings, IReadOnlyList<HqSyncRow> chunk)
    {
        var changes = new List<object>(chunk.Count);

        foreach (var row in chunk)
        {
            changes.Add(new
            {
                changeId = row.ChangeId,
                entityType = row.EntityType,
                action = row.Action,
                entityKey = row.EntityKey,
                storeId = row.StoreId,
                payload = ParsePayload(row.PayloadJson),
            });
        }

        var batch = new
        {
            device = string.IsNullOrWhiteSpace(settings.DeviceIdentifier) ? null : settings.DeviceIdentifier.Trim(),
            sentAt = DateTime.UtcNow,
            count = chunk.Count,
            changes,
        };

        return JsonSerializer.Serialize(batch, SendOpts);
    }

    /// <summary>A row's stored JSON as a nestable element; unreadable payloads go out as null.</summary>
    private static object? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How many rows the endpoint says it took. A 2xx with no parsable number is taken as
    /// "all of them", which matches HTTP semantics; a short count is honoured so the rest of the
    /// batch stays queued instead of being marked delivered.
    /// </summary>
    private static int ParseAcceptedCount(string? body, int chunkSize)
    {
        if (string.IsNullOrWhiteSpace(body)) return chunkSize;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out var plain))
                return plain;

            foreach (var name in new[] { "acceptedCount", "accepted", "count", "taken" })
            {
                if (root.ValueKind != JsonValueKind.Object) break;
                if (!root.TryGetProperty(name, out var value)) continue;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var s)) return s;
            }
        }
        catch (JsonException)
        {
            // A 2xx with a non-JSON body still means "taken".
        }

        return chunkSize;
    }

    // ---- request plumbing ----

    private static HttpRequestMessage BuildRequest(HttpMethod method, SyncSetting settings, string segment,
        HttpContent? content)
    {
        var request = new HttpRequestMessage(method, BuildUri(settings.BaseUrl, segment));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var device = settings.DeviceIdentifier?.Trim();
        if (!string.IsNullOrWhiteSpace(device))
            request.Headers.TryAddWithoutValidation(SyncSetting.DeviceHeaderName, device);

        // The only place the secret exists in this class: a header, never a URL, never a body,
        // never a message handed back to the caller.
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.TryAddWithoutValidation(SyncSetting.ApiKeyHeaderName, settings.ApiKey.Trim());

        if (content != null) request.Content = content;
        return request;
    }

    private static Uri BuildUri(string baseUrl, string segment)
    {
        var root = baseUrl.Trim();
        var hasTrailingSlash = root.EndsWith('/');
        return new Uri(hasTrailingSlash ? root + segment : $"{root}/{segment}");
    }

    /// <summary>Probe wait, always shorter than the send wait so opening a screen never stalls.</summary>
    private static TimeSpan ProbeWait(int configuredSeconds) =>
        TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 1, MaxProbeSeconds));

    // ---- verdicts in shopkeeper language ----

    /// <summary>
    /// Turns a status code into the sentence that tells the operator whether to retry, fix the
    /// settings, or phone HQ. Deliberately different per family: 401/403/404/4xx/5xx/redirect each
    /// name the thing to do next, and none of them can carry the key.
    /// </summary>
    private string DescribeResponse(HttpResponseMessage response, SyncSetting settings, bool healthy,
        string? snippet)
    {
        var host = SyncSetting.HostLabel(settings.BaseUrl);
        var code = (int)response.StatusCode;
        var tail = string.IsNullOrWhiteSpace(snippet) ? string.Empty : $" HQ said: {snippet}";

        if (healthy)
            return $"{host} answered {code} — reachable and accepting this till" +
                   $"{(string.IsNullOrWhiteSpace(settings.DeviceIdentifier) ? " (no device name set)" : $" as '{settings.DeviceIdentifier.Trim()}'")}.";

        // Switched on the numeric status because several HttpStatusCode names share one number
        // (Redirect and Found are both 302) and a duplicated label would not compile.
        return code switch
        {
            401 =>
                $"{host} refused the API key ({code}). The network is fine and your changes are safe; " +
                "the key is wrong, expired or belongs to another till. Paste the key HQ issued, save and " +
                $"test again.{tail}",
            403 =>
                $"{host} knows this key but will not let this till sync ({code}). Ask HQ to grant the " +
                $"device '{(string.IsNullOrWhiteSpace(settings.DeviceIdentifier) ? "(unnamed)" : settings.DeviceIdentifier.Trim())}' " +
                $"permission for change sync.{tail}",
            404 =>
                $"{host} has no sync endpoint at this address ({code}). The base address probably points " +
                $"at the wrong API root — it should be the folder that holds health and changes.{tail}",
            405 =>
                $"{host} rejected the request shape ({code}). HQ expects a different verb or path at this " +
                $"address; confirm the API root with your HQ integrator.{tail}",
            406 or 409 or 422 =>
                $"{host} refused the batch as it stood ({code}). Read the reason below, fix the offending " +
                $"document on the Offline / Sync Queue screen and retry.{tail}",
            408 or 504 =>
                $"{host} timed out ({code}). HQ is slow or its gateway gave up. Retry in a few minutes; " +
                $"the queue keeps every change in the meantime.{tail}",
            >= 300 and < 400 =>
                $"{host} redirected this till instead of answering ({code}). Redirects are refused so the " +
                "API key can never be forwarded to another server — save the final https:// address HQ " +
                "actually listens on.",
            >= 500 =>
                $"{host} is having server trouble ({code}). Nothing you can fix at the till: retry later " +
                $"and the queue will catch up.{tail}",
            >= 400 =>
                $"{host} answered but refused the request ({code}). Check the address and device name on " +
                $"the Sync Centre screen, then test again.{tail}",
            _ =>
                $"{host} answered {code}, which is not a usable sync response.{tail}",
        };
    }

    // ---- safety helpers ----

    /// <summary>
    /// Response body, cleaned for human eyes: the key removed if an echo ever leaked into a body,
    /// white space flattened and length capped, so the result is safe to show and to write to a run log.
    /// </summary>
    private static async Task<string?> ReadSnippetAsync(HttpResponseMessage response, SyncSetting settings)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            return Sanitize(text, settings.ApiKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Redacts the secret and shortens the text. Never returns null for a non-empty input.</summary>
    private static string? Sanitize(string? text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = text.Replace('\r', ' ').Replace('\n', ' ');
        while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
        cleaned = cleaned.Trim();

        var secret = apiKey?.Trim();
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
            cleaned = cleaned.Replace(secret, "\u2022\u2022\u2022\u2022");

        return cleaned.Length <= MaxSnippetChars
            ? cleaned
            : cleaned.Substring(0, MaxSnippetChars - 3) + "...";
    }

    /// <summary>
    /// Fresh configuration for every call, from a scope this transport owns — the reason it can live
    /// as a singleton without capturing the UI thread's DbContext. Also refreshes the transport
    /// selection so a settings change is picked up by the very next run.
    /// </summary>
    private async Task<SyncSetting?> LoadSettingsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await dbContext.SyncSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1)
                           ?? new SyncSetting { Id = 1 };

            _cached = settings;
            _selection.Refresh(settings);
            return settings;
        }
        catch
        {
            _selection.Refresh(null);
            return null;
        }
    }

    private static readonly JsonSerializerOptions SendOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
