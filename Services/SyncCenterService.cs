using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Linq;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-08: implementation of the sync control centre. Reads are cheap and local; every mutation is
/// audited; the run history is bounded so the screen never grows without end. It owns no queue
/// logic — replay, gating and the per-row attempt ceiling stay in <see cref="IOfflineQueueService"/>.
/// </summary>
public class SyncCenterService : ISyncCenterService
{
    /// <summary>Runs kept in SyncRunLogs. Older rows are trimmed after each write.</summary>
    public const int MaxHistoryRows = 200;

    private const int MaxMessageChars = 500;

    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IHqSyncTransport _transport;
    private readonly HqTransportSelection _selection;

    public string? LastValidationError { get; private set; }

    public List<string> Warnings { get; } = new();

    public SyncCenterService(AppDbContext dbContext, IAuthService authService, IAuditService auditService,
        IOfflineQueueService offlineQueue, IHqSyncTransport transport, HqTransportSelection selection)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _offlineQueue = offlineQueue;
        _transport = transport;
        _selection = selection;
    }

    // ---- transport choice ----

    public void PrimeTransportSelection()
    {
        try
        {
            // Synchronous on purpose: App.OnStartup must know which transport to hand the queue
            // before the first OfflineQueueService is constructed. One local SQLite row read.
            var settings = _dbContext.SyncSettings.AsNoTracking().FirstOrDefault(s => s.Id == 1)
                           ?? new SyncSetting { Id = 1 };
            _selection.Refresh(settings);
        }
        catch
        {
            // A database that is not ready yet must not stop the till: simulated endpoint it is.
            _selection.Refresh(null);
        }
    }

    // ---- settings ----

    public async Task<SyncSetting> GetSettingsAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        _selection.Refresh(settings);
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(bool isEnabled, string? baseUrl,
        string? apiKey, string? deviceIdentifier, bool useTlsOnly, string batchSizeLabel, string timeoutLabel)
    {
        LastValidationError = null;
        Warnings.Clear();

        if (!SyncSetting.BatchSizeOptions.Contains(batchSizeLabel))
            return (false, Fail($"Unknown batch size '{batchSizeLabel}'."));

        if (!SyncSetting.TimeoutOptions.Contains(timeoutLabel))
            return (false, Fail($"Unknown timeout '{timeoutLabel}'."));

        var address = (baseUrl ?? string.Empty).Trim();
        var device = (deviceIdentifier ?? string.Empty).Trim();

        // Validated even when live sync is off, so a half-typed address cannot hide until the day
        // somebody flips the switch during a rush.
        if (!string.IsNullOrEmpty(address) && !SyncSetting.TryValidateBaseUrl(address, useTlsOnly, out var urlError))
            return (false, Fail(urlError!));

        if (isEnabled && string.IsNullOrEmpty(address))
            return (false, Fail("Live HQ sync needs the server address before it can be switched on. " +
                                "Enter it, or leave live sync off and keep using the simulated endpoint."));

        if (isEnabled && string.IsNullOrEmpty(device))
            return (false, Fail("Live HQ sync needs a device name for this till, so HQ can tell the tills " +
                                "apart. Something like 'til-01-mumbai' is enough."));

        if (device.Contains(' '))
        {
            device = device.Replace(' ', '-');
            Warnings.Add("Spaces are awkward in a device name, so the device identifier was saved with " +
                         "dashes instead.");
        }

        // Blank box = keep the stored secret; the screen never re-displays it, so blank is normal.
        // Everything is validated BEFORE the tracked row is touched, so a refused save can never
        // leave half-written settings for the next SaveChanges in this context to persist.
        var settings = await GetOrCreateSettingsAsync();
        var typedKey = (apiKey ?? string.Empty).Trim();
        var keyChanged = false;

        if (typedKey.Length > 0)
        {
            if (typedKey.Contains('\u2022'))
                return (false, Fail("That looks like the masked placeholder, not a real key. Paste the API " +
                                    "key HQ gave you, without the dots."));

            if (typedKey.Length < SyncSetting.MinApiKeyLength)
                return (false, Fail($"The API key is only {typedKey.Length} characters — too short to be a " +
                                     $"real key (minimum {SyncSetting.MinApiKeyLength}). Paste the full value HQ issued."));

            keyChanged = !string.Equals(typedKey, (settings.ApiKey ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        var effectiveKey = typedKey.Length > 0 ? typedKey : (settings.ApiKey ?? string.Empty).Trim();

        if (isEnabled && effectiveKey.Length == 0)
            return (false, Fail("Live HQ sync is on but this till has no API key, so HQ would refuse every " +
                                "batch. Enter the key HQ issued, or switch live sync off."));

        if (!useTlsOnly && address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            Warnings.Add("TLS enforcement is off and the address is plain http://: the API key and every " +
                         "invoice travel unreadable on the network. Only accept this on a wired test network.");

        if (typedKey.Length > 0 && !keyChanged)
            Warnings.Add("The key you pasted is the one already stored, so nothing changed there.");

        var before = Snapshot(settings);

        settings.IsEnabled = isEnabled;
        settings.BaseUrl = Truncate(address, 300);
        settings.DeviceIdentifier = Truncate(device, 100);
        settings.UseTlsOnly = useTlsOnly;
        settings.BatchSize = SyncSetting.BatchSizeFor(batchSizeLabel);
        settings.TimeoutSeconds = SyncSetting.SecondsFor(timeoutLabel);
        if (typedKey.Length > 0) settings.ApiKey = Truncate(typedKey, 500);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, Fail("The sync connection could not be saved."));
        }

        _selection.Refresh(settings);

        await _auditService.RecordAsync("Update", "SyncSetting", settings.Id, "HQ sync connection",
            before, AfterSnapshot(settings, keyChanged));

        return (true, null);
    }

    // ---- test connection ----

    public async Task<SyncTestResult> TestConnectionAsync()
    {
        LastValidationError = null;

        var settings = await GetOrCreateSettingsAsync();
        _selection.Refresh(settings);

        bool reachable;
        try
        {
            reachable = await _transport.ProbeAsync();
        }
        catch (Exception ex)
        {
            return new SyncTestResult(false, $"Testing the connection threw {ex.GetType().Name}: " +
                                             $"{Redact(ex.Message, settings.ApiKey)}");
        }

        var detail = _transport is HttpHqSyncTransport http
            ? http.LastProbeDetail
            : SimulatedProbeMessage(settings, reachable);

        var head = _selection.IsLive
            ? (reachable ? "Live connection OK." : "Live connection not working.")
            : (reachable ? "Simulated endpoint answering — no real server contacted."
                         : "Simulated endpoint is standing by — no real server contacted.");

        return new SyncTestResult(reachable, $"{head} {detail}");
    }

    // ---- snapshot ----

    public async Task<SyncCenterSnapshot> GetSnapshotAsync(bool probe = true)
    {
        LastValidationError = null;

        var settings = await GetOrCreateSettingsAsync();
        _selection.Refresh(settings);

        var snapshot = new SyncCenterSnapshot
        {
            Settings = settings,
            Queue = await _offlineQueue.GetStatsAsync(),
            CountsByStatus = await _offlineQueue.GetCountsByStatusAsync(),
            CountsByEntityType = await GetCountsByEntityTypeAsync(),
            TransportName = _transport.EndpointName,
            IsLiveTransport = _selection.IsLive,
            TransportReason = _selection.Reason,
            LastRun = await _dbContext.SyncRunLogs.AsNoTracking()
                .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(),
        };

        if (probe)
        {
            try
            {
                snapshot.IsOnline = await _transport.ProbeAsync();
            }
            catch
            {
                snapshot.IsOnline = false;
            }

            snapshot.WasProbed = true;
            snapshot.ProbeDetail = _transport is HttpHqSyncTransport http
                ? http.LastProbeDetail
                : SimulatedProbeMessage(settings, snapshot.IsOnline);
        }
        else
        {
            snapshot.ProbeDetail = _transport is HttpHqSyncTransport http
                ? $"Last probe this session: {http.LastProbeDetail}"
                : "Press Test connection to ask the endpoint.";
        }

        snapshot.HasEverSynced = snapshot.LastRun != null
                                 || settings.LastSyncAt != null
                                 || snapshot.Queue.Synced > 0;

        snapshot.HealthText = BuildHealthText(snapshot);
        snapshot.ConfigurationText = BuildConfigurationText(settings, snapshot);

        return snapshot;
    }

    public async Task<List<SyncRunLog>> GetHistoryAsync(int take = 50)
    {
        var size = Math.Clamp(take, 1, 200);
        return await _dbContext.SyncRunLogs.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Take(size)
            .ToListAsync();
    }

    // ---- drive the queue ----

    public async Task<OfflineSyncOutcome> RunNowAsync(string trigger = "Manual")
    {
        Warnings.Clear();
        var startedAt = DateTime.UtcNow;
        var settings = await GetOrCreateSettingsAsync();
        var queuedBefore = (await _offlineQueue.GetStatsAsync()).Queued;

        var outcome = await _offlineQueue.FlushAsync(trigger);
        await RecordRunAsync(settings, outcome, trigger, startedAt, queuedBefore);

        foreach (var warning in _offlineQueue.Warnings) Warnings.Add(warning);
        return outcome;
    }

    public async Task<OfflineSyncOutcome> RetryFailedAsync()
    {
        Warnings.Clear();
        var startedAt = DateTime.UtcNow;
        var settings = await GetOrCreateSettingsAsync();
        var queuedBefore = (await _offlineQueue.GetStatsAsync()).Queued;

        var outcome = await _offlineQueue.RetryFailedAsync();
        await RecordRunAsync(settings, outcome, SyncRunLog.TriggerRetry, startedAt, queuedBefore);

        foreach (var warning in _offlineQueue.Warnings) Warnings.Add(warning);
        return outcome;
    }

    public async Task<(bool Success, int RemovedCount, string? Error)> ClearHistoryAsync()
    {
        LastValidationError = null;

        var runs = await _dbContext.SyncRunLogs.ToListAsync();
        if (runs.Count == 0) return (true, 0, null);

        _dbContext.SyncRunLogs.RemoveRange(runs);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, 0, Fail("The sync history could not be cleared."));
        }

        await _auditService.RecordAsync("Purge", "SyncRunLog", 0, "Sync run history cleared", null,
            new { RemovedCount = runs.Count });

        return (true, runs.Count, null);
    }

    public async Task<string> DescribeEffectiveConfigurationAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        _selection.Refresh(settings);

        var snapshot = new SyncCenterSnapshot
        {
            Settings = settings,
            Queue = await _offlineQueue.GetStatsAsync(),
            TransportName = _transport.EndpointName,
            IsLiveTransport = _selection.IsLive,
            TransportReason = _selection.Reason,
        };

        return BuildConfigurationText(settings, snapshot);
    }

    // ---- run history ----

    /// <summary>
    /// One row per run, including the runs that sent nothing (skipped/nothing-due), because "why did
    /// it not go through this morning" is the question the screen gets asked. Nothing secret is stored.
    /// </summary>
    private async Task RecordRunAsync(SyncSetting settings, OfflineSyncOutcome outcome, string trigger,
        DateTime startedAt, int queuedBefore)
    {
        var finishedAt = DateTime.UtcNow;

        var attemptedCount = outcome.Attempted
            ? outcome.SyncedCount + outcome.RejectedCount + outcome.FailedCount
            : 0;

        var verdict = MapOutcome(outcome, queuedBefore);
        var isCleanRun = verdict is SyncRunLog.OutcomeCompleted or SyncRunLog.OutcomeNothingDue;
        var runMessage = Redact(outcome.Message, settings.ApiKey);

        var log = new SyncRunLog
        {
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Trigger = Truncate(string.IsNullOrWhiteSpace(trigger) ? SyncRunLog.TriggerManual : trigger, 30),
            TransportName = Truncate(_transport.EndpointName, 120),
            AttemptedCount = attemptedCount,
            AcceptedCount = outcome.SyncedCount,
            RejectedCount = outcome.RejectedCount,
            FailedCount = outcome.FailedCount,
            RemainingCount = outcome.RemainingCount,
            Outcome = verdict,
            Error = isCleanRun ? null : Truncate(runMessage, MaxMessageChars),
            StoreId = _authService.CurrentUser?.StoreId,
            DeviceIdentifier = string.IsNullOrWhiteSpace(settings.DeviceIdentifier)
                ? null
                : Truncate(settings.DeviceIdentifier, 100),
        };

        if (outcome.Attempted)
        {
            settings.LastSyncAt = finishedAt;
            settings.LastResult = Truncate(runMessage, MaxMessageChars);
            settings.UpdatedAt = finishedAt;
            settings.UpdatedBy = _authService.CurrentUser?.Username;
        }
        else if (log.Outcome == SyncRunLog.OutcomeSkipped)
        {
            settings.LastResult = log.Error;
        }

        _dbContext.SyncRunLogs.Add(log);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // The sync itself already happened; losing the history row is a reporting problem.
            Warnings.Add($"The run finished but its history row could not be written: {Redact(ex.Message, settings.ApiKey)}");
            return;
        }

        await _auditService.RecordAsync("SyncRun", "SyncRunLog", log.Id,
            $"{log.Trigger} sync run — {log.Outcome}", null, new
            {
                log.Trigger,
                log.Outcome,
                log.TransportName,
                log.AttemptedCount,
                log.AcceptedCount,
                log.RejectedCount,
                log.FailedCount,
                log.RemainingCount,
                log.StoreId,
                log.DeviceIdentifier,
                log.Error,
            });

        await TrimHistoryAsync();
    }

    private static string MapOutcome(OfflineSyncOutcome outcome, int queuedBefore)
    {
        if (!outcome.Attempted)
            return queuedBefore == 0 ? SyncRunLog.OutcomeNothingDue : SyncRunLog.OutcomeSkipped;

        if (outcome.RejectedCount == 0 && outcome.FailedCount == 0)
            return SyncRunLog.OutcomeCompleted;

        if (outcome.SyncedCount > 0) return SyncRunLog.OutcomePartial;

        return SyncRunLog.OutcomeRejected;
    }

    /// <summary>Keep the newest <see cref="MaxHistoryRows"/> runs; a bounded table, not a log file.</summary>
    private async Task TrimHistoryAsync()
    {
        try
        {
            var staleIds = await _dbContext.SyncRunLogs
                .OrderByDescending(r => r.StartedAt)
                .ThenByDescending(r => r.Id)
                .Skip(MaxHistoryRows)
                .Select(r => r.Id)
                .ToListAsync();

            if (staleIds.Count == 0) return;

            await _dbContext.SyncRunLogs.Where(r => staleIds.Contains(r.Id))
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            Warnings.Add($"Sync history could not be trimmed to {MaxHistoryRows} rows: {ex.Message}");
        }
    }

    private async Task<Dictionary<string, int>> GetCountsByEntityTypeAsync()
    {
        var counts = PendingChange.EntityTypeOptions.ToDictionary(type => type, _ => 0);
        var rows = await _dbContext.PendingChanges.AsNoTracking().Select(p => p.EntityType).ToListAsync();

        foreach (var group in rows.GroupBy(type => type))
            counts[group.Key] = group.Count();

        return counts;
    }

    // ---- wording ----

    private string BuildHealthText(SyncCenterSnapshot snapshot)
    {
        if (!snapshot.IsLiveTransport)
        {
            var offline = snapshot.Queue.IsForcedOffline;
            return offline
                ? "Simulated endpoint — forced offline is ON"
                : "Simulated endpoint — no real server in use";
        }

        if (!snapshot.WasProbed) return "Live HQ endpoint — not probed yet";

        return snapshot.IsOnline
            ? "Live HQ endpoint reachable"
            : "Live HQ endpoint not reachable";
    }

    private string BuildConfigurationText(SyncSetting settings, SyncCenterSnapshot snapshot)
    {
        var queue = snapshot.Queue;

        var transport = snapshot.IsLiveTransport
            ? $"Runs go through the live transport ({Truncate(snapshot.TransportName, 120)}). The simulated endpoint is no longer used."
            : "Runs go through the simulated endpoint, so nothing leaves this till. " +
              "Switching live sync on with a usable address and key replaces it — one run then proves the link.";

        var offlineGate = queue.IsForcedOffline
            ? "Forced offline is ON, so even a working live link sends nothing until it is switched off on the Offline / Sync Queue screen."
            : queue.AutoSyncEnabled
                ? $"Forced offline is OFF and auto-sync replays {OfflineSetting.IntervalLabel(queue.AutoSyncMinutes).ToLowerInvariant()} " +
                  $"(ceiling {Math.Max(1, queue.MaxAttempts)} attempt(s) per change)."
                : "Forced offline is OFF; replay happens only when an operator presses Sync Now.";

        var depth = queue.PendingCount == 0
            ? "Nothing is waiting in the queue."
            : $"{queue.Queued} queued and {queue.Failed} failed change(s) waiting, oldest {queue.OldestPendingAge}.";

        return $"{SyncSetting.Describe(settings)} {transport} {snapshot.TransportReason} {offlineGate} {depth}";
    }

    private static string SimulatedProbeMessage(SyncSetting settings, bool reachable)
    {
        var why = settings.IsEnabled
            ? "The live endpoint is switched on but not usable yet, so the till stayed on the simulated endpoint."
            : "Live sync is switched off, so the till is using the simulated endpoint.";

        return reachable
            ? $"{why} The simulated endpoint always takes well-formed changes, which is how the queue and the " +
              "retry ceiling are exercised before a real server exists."
            : $"{why} The simulated endpoint reported itself unreachable, which happens only while forced " +
              "offline is ON — switch forced offline off on the Offline / Sync Queue screen.";
    }

    // ---- secret hygiene ----

    /// <summary>Scrub the stored key out of any text before it reaches a log row, a warning or a message.</summary>
    private static string Redact(string? text, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var cleaned = text.Trim();
        var secret = apiKey?.Trim();
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
            cleaned = cleaned.Replace(secret, "\u2022\u2022\u2022\u2022 (key redacted)");

        return cleaned.Length <= MaxMessageChars ? cleaned : cleaned.Substring(0, MaxMessageChars - 3) + "...";
    }

    // ---- internals ----

    private async Task<SyncSetting> GetOrCreateSettingsAsync()
    {
        var settings = await _dbContext.SyncSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new SyncSetting { Id = 1 };
            _dbContext.SyncSettings.Add(settings);
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                // Unsaved but usable in memory for this read; the next save retries the insert.
                Warnings.Add("The sync connection row could not be created yet, so defaults are in use " +
                             "for this screen only.");
            }
        }
        return settings;
    }

    private static object Snapshot(SyncSetting settings) => new
    {
        settings.IsEnabled,
        settings.BaseUrl,
        settings.DeviceIdentifier,
        settings.UseTlsOnly,
        settings.BatchSize,
        settings.TimeoutSeconds,
        HasKey = settings.HasApiKey,
        KeyLength = (settings.ApiKey ?? string.Empty).Trim().Length,
    };

    /// <summary>Audit payloads are built here, field by field, so no credential can slip into one.</summary>
    private static object AfterSnapshot(SyncSetting settings, bool keyChanged) => new
    {
        settings.IsEnabled,
        settings.BaseUrl,
        settings.DeviceIdentifier,
        settings.UseTlsOnly,
        settings.BatchSize,
        settings.TimeoutSeconds,
        HasKey = settings.HasApiKey,
        KeyLength = (settings.ApiKey ?? string.Empty).Trim().Length,
        KeyChanged = keyChanged,
    };

    private string Fail(string message)
    {
        LastValidationError = message;
        return message;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 3)) + "...";
}
