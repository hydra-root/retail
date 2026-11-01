using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-11: the local-first change queue (outbox). Business code enqueues what it just did;
/// the queue replays it to the HQ endpoint later, per-row, with an attempt ceiling.
/// Registered as a normal transient service — it works on the scoped AppDbContext like the
/// other services, so it must not be captured by a singleton.
/// </summary>
public interface IOfflineQueueService
{
    /// <summary>Status vocabulary for the screen's filter, "All" first.</summary>
    List<string> StatusFilters { get; }

    /// <summary>Entity vocabulary offered to the enqueue call sites (also used by the screen's filter).</summary>
    List<string> EntityTypes { get; }

    /// <summary>Human-readable reason for the most recent validation/persistence failure.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last enqueue/flush run (oversized payloads, dropped rows).</summary>
    List<string> Warnings { get; }

    Task<OfflineSetting> GetSettingsAsync();

    /// <summary>
    /// Persist the offline switches (labels come straight from the editor's ComboBoxes),
    /// validating them against the OfflineSetting vocabularies and auditing the change.
    /// </summary>
    Task<(bool Success, string? Error)> SaveSettingsAsync(bool isForcedOffline, bool autoSyncEnabled,
        string intervalLabel, string maxAttemptsLabel);

    /// <summary>
    /// Append one local change to the queue. Never throws — billing must keep working when
    /// the queue itself cannot be written, so problems surface as (false, error) plus
    /// <see cref="LastValidationError"/>. <paramref name="payload"/> is serialized as JSON;
    /// <paramref name="storeId"/> defaults to the logged-in user's store.
    /// </summary>
    Task<(bool Success, string? Error)> EnqueueAsync(string entityType, string action, string entityKey,
        object payload, int? storeId = null);

    /// <summary>Oldest replayable rows — what the next run will send, for the status card.</summary>
    Task<List<PendingChange>> PeekAsync(int count = 25);

    /// <summary>Paged queue view, oldest first; status/entityType accept "All" or null to skip the filter.</summary>
    Task<List<PendingChange>> GetPageAsync(int page = 0, int pageSize = 100, string? status = null,
        string? entityType = null, string? search = null);

    /// <summary>Row counts keyed by PendingChange.Status (every vocabulary key is present, zeroed).</summary>
    Task<Dictionary<string, int>> GetCountsByStatusAsync();

    Task<OfflineQueueStats> GetStatsAsync();

    /// <summary>Connectivity step the screen shows: false while forced offline or the probe fails.</summary>
    Task<bool> IsOnlineAsync();

    /// <summary>Replay Queued rows in CreatedAt order through the transport; see <see cref="OfflineSyncOutcome"/>.</summary>
    Task<OfflineSyncOutcome> FlushAsync(string trigger = "Manual");

    /// <summary>Hand the Failed rows a fresh attempt budget, re-queue them and run one flush.</summary>
    Task<OfflineSyncOutcome> RetryFailedAsync();

    /// <summary>Drop one row from replay. Synced rows cannot be discarded — they are already delivered.</summary>
    Task<(bool Success, string? Error)> DiscardAsync(int changeId, string? reason = null);

    /// <summary>Delete every Synced row (history trim; frees the queue for the next busy day).</summary>
    Task<(bool Success, int PurgedCount, string? Error)> PurgeSyncedAsync();

    /// <summary>
    /// Timer-friendly entry point: flush only when auto-sync is ON, the interval has elapsed and
    /// something is actually queued. Swallows its own exceptions, so the existing backup/reminder
    /// tick can call it fire-and-forget. Returns true when a row was handed to the endpoint.
    /// </summary>
    Task<bool> RunAutoSyncAsync();
}

/// <summary>STK-11: aggregate of one replay run.</summary>
public class OfflineSyncOutcome
{
    /// <summary>False when the run never reached the endpoint (offline skip, empty queue).</summary>
    public bool Attempted { get; set; }

    public int SyncedCount { get; set; }

    /// <summary>Rows rejected this run that still have attempts left, so they stay Queued.</summary>
    public int RejectedCount { get; set; }

    /// <summary>Rows that hit the attempt ceiling this run and are now Failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Rows left in the queue after the run (queued + failed).</summary>
    public int RemainingCount { get; set; }

    /// <summary>One-line reading of the run, also stored as OfflineSetting.LastResult.</summary>
    public string Message { get; set; } = string.Empty;

    public string Summary => Attempted
        ? $"{SyncedCount} synced, {RejectedCount} rejected, {FailedCount} newly failed, {RemainingCount} still queued."
        : $"Not attempted — {Message}";
}

/// <summary>STK-11: everything the status card shows, in one read.</summary>
public class OfflineQueueStats
{
    public int Total { get; set; }
    public int Queued { get; set; }
    public int Synced { get; set; }
    public int Failed { get; set; }
    public int Discarded { get; set; }

    /// <summary>Age of the oldest row still waiting; null when nothing is queued.</summary>
    public DateTime? OldestQueuedAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public string? LastResult { get; set; }

    public bool IsForcedOffline { get; set; }

    public bool AutoSyncEnabled { get; set; }

    public int AutoSyncMinutes { get; set; }

    public int MaxAttempts { get; set; }

    public int PendingCount => Queued + Failed;

    /// <summary>Wall-clock age of the oldest pending change, e.g. "42 min", "3 h", "just now".</summary>
    public string OldestPendingAge
    {
        get
        {
            if (OldestQueuedAt == null) return "nothing waiting";
            var age = DateTime.UtcNow - OldestQueuedAt.Value;
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min";
            if (age.TotalDays < 1) return $"{(int)age.TotalHours} h";
            return $"{(int)age.TotalDays} day(s)";
        }
    }
}
