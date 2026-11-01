using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-11: local-first outbox. Work is written down first (EnqueueAsync) and replayed to the
/// HQ endpoint later (FlushAsync), oldest change first, one attempt budget per row.
/// The queue logic is transport-agnostic: it only ever asks <see cref="IHqSyncTransport"/>
/// whether the endpoint answers and whether it accepted the batch, so a real HTTP client can
/// replace <see cref="SimulatedHqSyncTransport"/> without touching this class.
/// </summary>
public class OfflineQueueService : IOfflineQueueService
{
    /// <summary>Rows handed to the endpoint per run; the rest wait for the next run.</summary>
    private const int MaxRowsPerFlush = 200;

    /// <summary>Payload cap. A longer document is stored truncated and the operator is warned.</summary>
    private const int MaxPayloadChars = 100_000;

    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IHqSyncTransport _transport;

    public List<string> StatusFilters { get; } = PendingChange.StatusOptions.Prepend("All").ToList();

    public List<string> EntityTypes { get; } = PendingChange.EntityTypeOptions.Prepend("All").ToList();

    public string? LastValidationError { get; private set; }

    public List<string> Warnings { get; } = new();

    public OfflineQueueService(AppDbContext dbContext, IAuthService authService,
        IAuditService auditService, IHqSyncTransport transport)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _transport = transport;
    }

    // ---- settings ----

    public async Task<OfflineSetting> GetSettingsAsync() => await GetOrCreateSettingsAsync();

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(bool isForcedOffline,
        bool autoSyncEnabled, string intervalLabel, string maxAttemptsLabel)
    {
        LastValidationError = null;

        if (!OfflineSetting.AutoSyncIntervalOptions.Contains(intervalLabel))
            return (false, Fail($"Unknown auto-sync interval '{intervalLabel}'."));

        if (!OfflineSetting.MaxAttemptOptions.Contains(maxAttemptsLabel))
            return (false, Fail($"Unknown attempt ceiling '{maxAttemptsLabel}'."));

        var settings = await GetOrCreateSettingsAsync();
        var before = Snapshot(settings);

        settings.IsForcedOffline = isForcedOffline;
        settings.AutoSyncEnabled = autoSyncEnabled;
        settings.AutoSyncMinutes = OfflineSetting.MinutesForInterval(intervalLabel);
        settings.MaxAttempts = OfflineSetting.AttemptsFor(maxAttemptsLabel);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, Fail("Failed to save the offline sync settings."));
        }

        await _auditService.RecordAsync("Update", "OfflineSetting", settings.Id, "Offline / sync settings",
            before, Snapshot(settings));

        return (true, null);
    }

    // ---- enqueue (the local-first half) ----

    public async Task<(bool Success, string? Error)> EnqueueAsync(string entityType, string action,
        string entityKey, object payload, int? storeId = null)
    {
        LastValidationError = null;
        var label = $"{entityType} {action}";

        try
        {
            if (string.IsNullOrWhiteSpace(entityType))
                return (false, Fail("Cannot queue a change without an entity type."));

            if (!PendingChange.ActionOptions.Contains(action))
                return (false, Fail($"Unknown change action '{action}' for {entityType}."));

            if (!PendingChange.EntityTypeOptions.Contains(entityType.Trim()))
                Warnings.Add($"{entityType} is not in the queue's entity vocabulary yet; the row is still queued.");

            var change = new PendingChange
            {
                EntityType = entityType.Trim(),
                Action = action,
                EntityKey = Truncate(string.IsNullOrWhiteSpace(entityKey) ? "(no key)" : entityKey.Trim(), 100),
                StoreId = storeId ?? _authService.CurrentUser?.StoreId,
                PayloadJson = SerializePayload(entityType, entityKey, payload),
                Status = PendingChange.StatusQueued,
                CreatedAt = DateTime.UtcNow,
            };

            _dbContext.PendingChanges.Add(change);
            await _dbContext.SaveChangesAsync();

            await _auditService.RecordAsync("Enqueue", "PendingChange", change.Id, change.RowLabel,
                null, new { change.EntityType, change.Action, change.EntityKey, change.StoreId, change.Status });

            return (true, null);
        }
        catch (Exception ex)
        {
            // Queueing must never break billing: the local document is already committed, so
            // report the problem instead of throwing back into the caller's success path.
            return (false, Fail($"Could not queue {label}: {ex.Message}"));
        }
    }

    // ---- read side ----

    public async Task<List<PendingChange>> PeekAsync(int count = 25) =>
        await _dbContext.PendingChanges.AsNoTracking()
            .Where(p => p.Status == PendingChange.StatusQueued || p.Status == PendingChange.StatusFailed)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(Math.Clamp(count, 1, 50))
            .ToListAsync();

    public async Task<List<PendingChange>> GetPageAsync(int page = 0, int pageSize = 100,
        string? status = null, string? entityType = null, string? search = null)
    {
        var query = _dbContext.PendingChanges.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(entityType) && entityType != "All")
            query = query.Where(p => p.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            query = query.Where(p => p.EntityKey.Contains(text)
                                  || p.EntityType.Contains(text)
                                  || (p.LastError != null && p.LastError.Contains(text)));
        }

        var size = Math.Clamp(pageSize, 1, 500);
        return await query
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(Math.Max(0, page) * size)
            .Take(size)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetCountsByStatusAsync()
    {
        var counts = PendingChange.StatusOptions.ToDictionary(status => status, _ => 0);
        var statuses = await _dbContext.PendingChanges.AsNoTracking().Select(p => p.Status).ToListAsync();
        foreach (var group in statuses.GroupBy(s => s))
            counts[group.Key] = group.Count();
        return counts;
    }

    public async Task<OfflineQueueStats> GetStatsAsync()
    {
        var rows = await _dbContext.PendingChanges.AsNoTracking()
            .Select(p => new { p.Status, p.CreatedAt })
            .ToListAsync();

        var settings = await GetOrCreateSettingsAsync();

        return new OfflineQueueStats
        {
            Total = rows.Count,
            Queued = rows.Count(r => r.Status == PendingChange.StatusQueued),
            Synced = rows.Count(r => r.Status == PendingChange.StatusSynced),
            Failed = rows.Count(r => r.Status == PendingChange.StatusFailed),
            Discarded = rows.Count(r => r.Status == PendingChange.StatusDiscarded),
            OldestQueuedAt = rows.Where(r => r.Status == PendingChange.StatusQueued)
                .Select(r => (DateTime?)r.CreatedAt).Min(),
            LastSyncAt = settings.LastSyncAt,
            LastResult = settings.LastResult,
            IsForcedOffline = settings.IsForcedOffline,
            AutoSyncEnabled = settings.AutoSyncEnabled,
            AutoSyncMinutes = settings.AutoSyncMinutes,
            MaxAttempts = settings.MaxAttempts,
        };
    }

    public async Task<bool> IsOnlineAsync()
    {
        try
        {
            return await _transport.ProbeAsync();
        }
        catch
        {
            return false;
        }
    }

    // ---- replay (the sync-later half) ----

    public async Task<OfflineSyncOutcome> FlushAsync(string trigger = "Manual")
    {
        LastValidationError = null;
        Warnings.Clear();

        var settings = await GetOrCreateSettingsAsync();
        var ceiling = Math.Max(1, settings.MaxAttempts);

        var batch = await _dbContext.PendingChanges
            .Where(p => p.Status == PendingChange.StatusQueued)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(MaxRowsPerFlush)
            .ToListAsync();

        if (batch.Count == 0)
            return new OfflineSyncOutcome
            {
                Attempted = false,
                Message = "Nothing to sync — no change is waiting in the queue."
            };

        // Connectivity step: while the endpoint does not answer nothing is attempted, so an
        // outage never burns the rows' attempt budget.
        if (!await _transport.ProbeAsync())
            return await SkipOfflineAsync(settings, trigger, batch.Count);

        var rows = batch.Select(p => new HqSyncRow(
            p.Id, p.EntityType, p.Action, p.EntityKey, p.StoreId, p.PayloadJson)).ToList();

        HqSyncResult result;
        try
        {
            result = await _transport.SendAsync(rows);
        }
        catch (Exception ex)
        {
            // A transport that throws is a rejected batch, not a crashed app.
            result = new HqSyncResult(false, 0, $"{_transport.EndpointName} threw {ex.GetType().Name}: {ex.Message}");
        }

        var now = DateTime.UtcNow;
        var acceptedCount = Math.Clamp(result.AcceptedCount, 0, batch.Count);
        var outcome = new OfflineSyncOutcome { Attempted = true };

        for (var i = 0; i < batch.Count; i++)
        {
            var row = batch[i];
            row.AttemptedAt = now;
            row.Attempts++;

            if (result.Accepted && i < acceptedCount)
            {
                row.Status = PendingChange.StatusSynced;
                row.SyncedAt = now;
                row.LastError = null;
                outcome.SyncedCount++;
                continue;
            }

            row.LastError = Truncate(result.Error ?? "The HQ endpoint did not accept this change.", 500);

            if (row.Attempts >= ceiling)
            {
                row.Status = PendingChange.StatusFailed;
                outcome.FailedCount++;
            }
            else
            {
                outcome.RejectedCount++;
            }
        }

        outcome.Message = BuildRunMessage(trigger, _transport.EndpointName, acceptedCount, batch.Count, ceiling, outcome);
        settings.LastSyncAt = now;
        settings.LastResult = Truncate(outcome.Message, 500);
        settings.UpdatedAt = now;
        settings.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            outcome.Message = Fail($"The sync ran but the queue could not be saved: {ex.Message}");
        }

        outcome.RemainingCount = await _dbContext.PendingChanges.AsNoTracking()
            .CountAsync(p => p.Status == PendingChange.StatusQueued || p.Status == PendingChange.StatusFailed);

        await _auditService.RecordAsync("Sync", "OfflineQueue", settings.Id, "HQ sync run", null,
            new { Trigger = trigger, result.Accepted, outcome.SyncedCount, outcome.RejectedCount,
                  outcome.FailedCount, outcome.RemainingCount, outcome.Message });

        return outcome;
    }

    public async Task<OfflineSyncOutcome> RetryFailedAsync()
    {
        LastValidationError = null;

        var settings = await GetOrCreateSettingsAsync();
        var failed = await _dbContext.PendingChanges
            .Where(p => p.Status == PendingChange.StatusFailed)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync();

        if (failed.Count == 0)
            return new OfflineSyncOutcome { Attempted = false, Message = "No failed change to retry." };

        foreach (var row in failed)
        {
            row.Status = PendingChange.StatusQueued;
            row.Attempts = 0; // the operator grants a fresh budget
            row.LastError = null;
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return new OfflineSyncOutcome
            {
                Attempted = false,
                Message = Fail("Failed rows could not be re-queued; nothing was sent.")
            };
        }

        await _auditService.RecordAsync("Retry", "OfflineQueue", settings.Id, "Re-queued failed changes",
            null, new { RetriedCount = failed.Count, Ceiling = settings.MaxAttempts });

        return await FlushAsync("Retry failed");
    }

    public async Task<(bool Success, string? Error)> DiscardAsync(int changeId, string? reason = null)
    {
        LastValidationError = null;

        var row = await _dbContext.PendingChanges.FirstOrDefaultAsync(p => p.Id == changeId);
        if (row == null)
            return (false, Fail($"Queue row #{changeId} no longer exists."));

        if (row.Status == PendingChange.StatusDiscarded)
            return (false, Fail($"{row.RowLabel} is already discarded."));

        if (row.Status == PendingChange.StatusSynced)
            return (false, Fail($"{row.RowLabel} was already accepted by HQ — discard is only for undelivered changes."));

        var before = new { row.Status, row.LastError, row.Attempts };

        row.Status = PendingChange.StatusDiscarded;
        row.AttemptedAt ??= DateTime.UtcNow;
        row.LastError = Truncate(string.IsNullOrWhiteSpace(reason)
            ? "Discarded by " + (_authService.CurrentUser?.Username ?? "operator")
            : reason.Trim(), 500);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, Fail($"Could not discard {row.RowLabel}."));
        }

        await _auditService.RecordAsync("Discard", "PendingChange", row.Id, row.RowLabel,
            before, new { row.Status, row.LastError });

        return (true, null);
    }

    public async Task<(bool Success, int PurgedCount, string? Error)> PurgeSyncedAsync()
    {
        LastValidationError = null;

        var synced = await _dbContext.PendingChanges
            .Where(p => p.Status == PendingChange.StatusSynced)
            .ToListAsync();

        if (synced.Count == 0)
            return (true, 0, null);

        _dbContext.PendingChanges.RemoveRange(synced);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, 0, Fail("Delivered rows could not be removed from the queue."));
        }

        var settings = await GetOrCreateSettingsAsync();
        await _auditService.RecordAsync("Purge", "OfflineQueue", settings.Id, "Purged delivered changes",
            null, new { PurgedCount = synced.Count });

        return (true, synced.Count, null);
    }

    public async Task<bool> RunAutoSyncAsync()
    {
        try
        {
            var settings = await GetOrCreateSettingsAsync();
            if (!settings.AutoSyncEnabled || settings.IsForcedOffline) return false;

            var interval = TimeSpan.FromMinutes(Math.Max(1, settings.AutoSyncMinutes));
            if (settings.LastSyncAt != null && DateTime.UtcNow < settings.LastSyncAt.Value + interval)
                return false;

            if (!await _dbContext.PendingChanges.AnyAsync(p => p.Status == PendingChange.StatusQueued))
                return false;

            var outcome = await FlushAsync("Auto");
            return outcome.Attempted && outcome.SyncedCount > 0;
        }
        catch
        {
            // Called from the app's existing timer plumbing: never let a tick throw.
            return false;
        }
    }

    // ---- internals ----

    private async Task<OfflineSetting> GetOrCreateSettingsAsync()
    {
        var settings = await _dbContext.OfflineSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new OfflineSetting { Id = 1 };
            _dbContext.OfflineSettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }
        return settings;
    }

    private async Task<OfflineSyncOutcome> SkipOfflineAsync(OfflineSetting settings, string trigger, int batchCount)
    {
        var stillQueued = await _dbContext.PendingChanges.AsNoTracking()
            .CountAsync(p => p.Status == PendingChange.StatusQueued);

        var message = $"{trigger} sync skipped: {_transport.EndpointName} is not reachable. " +
                      $"{stillQueued} change(s) stay queued and no attempts were used " +
                      $"({batchCount} were ready for this run).";

        settings.LastResult = Truncate(message, 500);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch { /* the skip reason is still shown from memory for this run */ }

        await _auditService.RecordAsync("Sync", "OfflineQueue", settings.Id, "HQ sync run skipped (offline)",
            null, new { Trigger = trigger, Online = false, Queued = stillQueued, Message = message });

        return new OfflineSyncOutcome
        {
            Attempted = false,
            RemainingCount = stillQueued,
            Message = message,
        };
    }

    private string? SerializePayload(string entityType, string entityKey, object payload)
    {
        if (payload == null)
        {
            Warnings.Add($"{entityType} '{entityKey}' was enqueued without a payload; " +
                         "the row will be rejected by HQ until the call site passes the document.");
            return null;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, WriteOpts);
        }
        catch (Exception ex)
        {
            // Queue the row with no payload: the endpoint will reject it, so the failure lands
            // in the queue grid instead of being silently dropped here.
            Warnings.Add($"Payload of {entityType} '{entityKey}' could not be serialized ({ex.Message}); " +
                         "the row is queued with an empty payload and will be rejected by HQ.");
            return null;
        }

        if (json.Length > MaxPayloadChars)
        {
            Warnings.Add($"Payload of {entityType} '{entityKey}' truncated from {json.Length} to " +
                         $"{MaxPayloadChars} characters; the HQ copy is incomplete.");
            return json.Substring(0, MaxPayloadChars);
        }

        return json;
    }

    private static string BuildRunMessage(string trigger, string endpoint, int acceptedCount, int batchSize,
        int ceiling, OfflineSyncOutcome outcome)
    {
        var head = acceptedCount == batchSize
            ? $"{trigger} sync: {endpoint} accepted all {batchSize} change(s)."
            : $"{trigger} sync: {endpoint} accepted {acceptedCount} of {batchSize} change(s)." +
              $" {outcome.RejectedCount} will be retried, {outcome.FailedCount} reached the " +
              $"{ceiling}-attempt ceiling and are now Failed.";

        return head;
    }

    private static object Snapshot(OfflineSetting settings) => new
    {
        settings.IsForcedOffline,
        settings.AutoSyncEnabled,
        settings.AutoSyncMinutes,
        settings.MaxAttempts,
    };

    private string Fail(string message)
    {
        LastValidationError = message;
        return message;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 3)) + "...";

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// STK-11: the far end of the queue, and the only thing the queue knows about HQ. Swapping
/// <see cref="SimulatedHqSyncTransport"/> for an HTTP client is one class — FlushAsync keeps
/// asking "does it answer?" then "did you take these rows?".
/// </summary>
public interface IHqSyncTransport
{
    /// <summary>Name shown on the status card and in every sync message.</summary>
    string EndpointName { get; }

    /// <summary>Connectivity step. False means "do not attempt anything this run".</summary>
    Task<bool> ProbeAsync();

    /// <summary>Hand one ordered batch to the endpoint and report how many rows it accepted.</summary>
    Task<HqSyncResult> SendAsync(IReadOnlyList<HqSyncRow> rows);
}

/// <summary>STK-11: one queue row as the endpoint sees it — deliberately a flat record, not the EF entity.</summary>
public record HqSyncRow(int ChangeId, string EntityType, string Action, string EntityKey,
    int? StoreId, string? PayloadJson);

/// <summary>STK-11: the endpoint's verdict on a batch.</summary>
public record HqSyncResult(bool Accepted, int AcceptedCount, string? Error);

/// <summary>
/// STK-11: stand-in for the real HQ sync endpoint, so the queue, the retry ceiling and the
/// reports are genuinely exercised before any server exists. It is unreachable while forced
/// offline is on; otherwise it accepts the batch — and rejects it when a row carries no or
/// unreadable JSON, which is what pushes rows to Failed the same way a real 400 would.
/// </summary>
public class SimulatedHqSyncTransport : IHqSyncTransport
{
    // Simulated latency, so the screen's busy state is real rather than instantaneous.
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SendDelay = TimeSpan.FromMilliseconds(250);

    private readonly AppDbContext _dbContext;

    public SimulatedHqSyncTransport(AppDbContext dbContext) => _dbContext = dbContext;

    public string EndpointName => "HQ sync endpoint (simulated)";

    public async Task<bool> ProbeAsync()
    {
        await Task.Delay(ProbeDelay);
        return !await IsForcedOfflineAsync();
    }

    public async Task<HqSyncResult> SendAsync(IReadOnlyList<HqSyncRow> rows)
    {
        await Task.Delay(SendDelay);

        if (rows.Count == 0)
            return new HqSyncResult(true, 0, null);

        if (await IsForcedOfflineAsync())
            return new HqSyncResult(false, 0, "HQ endpoint unreachable: forced offline is ON.");

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.PayloadJson))
                return new HqSyncResult(false, 0, $"{row.EntityType} '{row.EntityKey}' arrived with no payload.");

            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson);
            }
            catch (JsonException ex)
            {
                return new HqSyncResult(false, 0,
                    $"{row.EntityType} '{row.EntityKey}' payload is not valid JSON ({ex.Message}).");
            }
        }

        return new HqSyncResult(true, rows.Count, null);
    }

    private async Task<bool> IsForcedOfflineAsync()
    {
        var settings = await _dbContext.OfflineSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        return settings?.IsForcedOffline ?? false;
    }
}
