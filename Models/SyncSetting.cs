using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// SEC-08: singleton connection row (only Id == 1 is used) holding everything the live HTTP
/// transport needs to reach the HQ sync service: where it is, which secret proves this till,
/// which name HQ sees, how big a POST may be and how long the till may wait for an answer.
/// <see cref="ApiKey"/> is a credential: it is stored for the transport to use, but it is never
/// echoed to the screen, written to a log line, put in an audit detail or included in a message —
/// everything user-facing goes through <see cref="Mask"/>.
/// </summary>
public class SyncSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Master switch of the live transport. False means the simulated endpoint replays the queue.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>API root of the HQ sync service, e.g. https://hq.example.com/api/v1 — never contains a secret.</summary>
    [MaxLength(300)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Shared secret sent as the X-API-Key header. Read-only for the UI: see the class summary.</summary>
    [MaxLength(500)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Stable per-till name HQ uses to attribute a batch; sent as X-Device-Id.</summary>
    [MaxLength(100)]
    public string DeviceIdentifier { get; set; } = string.Empty;

    /// <summary>Refuse http:// addresses. Switchable off only so a wired test server on a LAN can be reached.</summary>
    public bool UseTlsOnly { get; set; } = true;

    /// <summary>Queue rows per HTTPS call; the transport splits a run into calls of this size.</summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>Seconds a probe or a POST may take before the till gives up on it.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>UTC stamp of the last run this screen recorded; mirrors OfflineSetting for the live path.</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Human-readable outcome of the last run (never contains the key).</summary>
    [MaxLength(500)]
    public string? LastResult { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    // ---- option vocabularies shared by the transport, the service and the editor ----

    /// <summary>Batch-size labels the editor binds; each maps 1:1 onto <see cref="BatchSize"/>.</summary>
    public static List<string> BatchSizeOptions { get; } = new()
    {
        "25 changes per call", "50 changes per call", "100 changes per call", "200 changes per call"
    };

    /// <summary>Timeout labels the editor binds; each maps 1:1 onto <see cref="TimeoutSeconds"/>.</summary>
    public static List<string> TimeoutOptions { get; } = new()
    {
        "5 seconds", "10 seconds", "15 seconds", "30 seconds", "60 seconds"
    };

    public const int DefaultBatchSize = 100;
    public const int DefaultTimeoutSeconds = 15;
    public const int MinApiKeyLength = 8;

    /// <summary>Header the live transport puts the key in. Kept here so the service can talk about it.</summary>
    public const string ApiKeyHeaderName = "X-API-Key";

    /// <summary>Header carrying <see cref="DeviceIdentifier"/>.</summary>
    public const string DeviceHeaderName = "X-Device-Id";

    /// <summary>Leading number of a batch label ("200 changes per call" → 200); unknown labels use the default.</summary>
    public static int BatchSizeFor(string batchSizeLabel) =>
        int.TryParse(batchSizeLabel.Split(' ')[0], out var size) ? Math.Clamp(size, 1, 500) : DefaultBatchSize;

    /// <summary>Closest vocabulary label for a stored batch size, so the ComboBox always matches an item.</summary>
    public static string BatchSizeLabel(int batchSize) =>
        BatchSizeOptions.MinBy(option => Math.Abs(BatchSizeFor(option) - Math.Clamp(batchSize, 1, 500)))
        ?? BatchSizeOptions[0];

    /// <summary>Leading number of a timeout label ("30 seconds" → 30); unknown labels use the default.</summary>
    public static int SecondsFor(string timeoutLabel) =>
        int.TryParse(timeoutLabel.Split(' ')[0], out var seconds) ? Math.Clamp(seconds, 1, 300) : DefaultTimeoutSeconds;

    /// <summary>Closest vocabulary label for a stored timeout, so the ComboBox always matches an item.</summary>
    public static string TimeoutLabel(int timeoutSeconds) =>
        TimeoutOptions.MinBy(option => Math.Abs(SecondsFor(option) - Math.Clamp(timeoutSeconds, 1, 300)))
        ?? TimeoutOptions[0];

    /// <summary>
    /// Plain-language validation of the address the operator typed. The transport refuses to run
    /// with an address that fails here, so a mistake shows up as one readable sentence instead of
    /// a stack trace or a silently dead sync.
    /// </summary>
    public static bool TryValidateBaseUrl(string? baseUrl, bool useTlsOnly, out string? error)
    {
        error = null;
        var text = baseUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter the HQ sync address, for example https://hq.example.com/api/v1";
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = $"'{Truncate(text, 80)}' is not a complete web address — it must start with https:// " +
                    "and name the server, like https://hq.example.com/api/v1";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "The HQ sync address has no server name, so there is nothing to connect to. " +
                    "It should look like https://hq.example.com/api/v1";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && useTlsOnly)
        {
            error = "Plain http:// is refused: the API key and your invoices would travel unreadable. " +
                    "Use the https:// address HQ gave you, or switch off 'TLS only' only for a wired test network.";
            return false;
        }

        return true;
    }

    /// <summary>True when the address is usable on its own terms, ignoring the master switch.</summary>
    [NotMapped]
    public bool IsEndpointUsable => TryValidateBaseUrl(BaseUrl, UseTlsOnly, out _);

    /// <summary>True when the live transport is both switched on and pointed at a usable address.</summary>
    [NotMapped]
    public bool IsLiveConfigured => IsEnabled && IsEndpointUsable;

    /// <summary>True when a secret is actually stored; the UI shows only this and <see cref="ApiKeyMasked"/>.</summary>
    [NotMapped]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    [NotMapped]
    public string ApiKeyMasked => Mask(ApiKey);

    [NotMapped]
    public string ConnectionSummary => Describe(this);

    /// <summary>
    /// Stripe-style mask: the tail only. Enough for an operator to tell two keys apart, never
    /// enough to use one. Blank and over-short values are fully masked.
    /// </summary>
    public static string Mask(string? apiKey)
    {
        var text = apiKey?.Trim() ?? string.Empty;
        if (text.Length == 0) return "(no key stored)";
        if (text.Length <= 4) return new string('\u2022', text.Length);
        return new string('\u2022', 8) + text.Substring(text.Length - 4);
    }

    /// <summary>Host-only reading of the address, safe to show in a status card or a message.</summary>
    public static string HostLabel(string? baseUrl)
    {
        var text = baseUrl?.Trim() ?? string.Empty;
        if (text.Length == 0) return "(no address saved)";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return Truncate(text, 60);
        return $"{uri.Host}{(uri.IsDefaultPort ? string.Empty : ":" + uri.Port)}{uri.PathAndQuery.TrimEnd('/')}";
    }

    /// <summary>
    /// Plain-language reading of the connection, so the screen can state what would run and why
    /// instead of leaving the operator to guess whether the till is talking to HQ or to a stand-in.
    /// </summary>
    public static string Describe(SyncSetting setting)
    {
        if (!setting.IsEnabled)
            return "Live HQ sync is OFF: every run replays the queue through the built-in simulated " +
                   "endpoint, so nothing leaves this till and no server is contacted.";

        if (!TryValidateBaseUrl(setting.BaseUrl, setting.UseTlsOnly, out var error))
            return $"Live HQ sync is switched ON but the address is not usable yet — {error} " +
                   "Until it is fixed, runs keep using the simulated endpoint.";

        if (!setting.HasApiKey)
            return $"Live HQ sync is switched ON and {HostLabel(setting.BaseUrl)} is a valid address, " +
                   "but no API key is stored, so the endpoint would refuse the till. Enter the key HQ " +
                   "issued, or switch live sync off.";

        var device = string.IsNullOrWhiteSpace(setting.DeviceIdentifier) ? "no device name" : setting.DeviceIdentifier.Trim();
        var tls = setting.UseTlsOnly ? "HTTPS enforced" : "TLS enforcement switched off by the operator";
        var timeout = Math.Clamp(setting.TimeoutSeconds, 1, 300);
        var batch = Math.Clamp(setting.BatchSize, 1, 500);

        return $"Live HQ sync is ON: runs go to {HostLabel(setting.BaseUrl)} as device '{device}' " +
               $"({tls}), {batch} change(s) per call, {timeout}-second wait, API key stored " +
               $"({setting.ApiKey.Trim().Length} characters).";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 3)) + "...";
}
