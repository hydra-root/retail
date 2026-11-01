using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-08: the sync control centre — everything the operator needs to know about the link to HQ
/// and nothing the queue already reports. It aggregates (a) which transport the container handed
/// the queue and why, (b) whether that endpoint answers, (c) queue depth by status and by document
/// type, (d) the outcome of the last run and (e) a bounded history of runs. It drives work only by
/// delegating to <see cref="IOfflineQueueService"/>, so the retry ceiling, per-row accounting and
/// the offline gate stay in one place.
/// It never upgrades the till from simulated to live by itself: the transport follows the saved
/// <see cref="SyncSetting"/> row and <see cref="SyncCenterSnapshot.TransportReason"/> says, in words,
/// what will run on the next Sync Now.
/// </summary>
public interface ISyncCenterService
{
    /// <summary>Reason of the most recent validation/persistence failure, shown inline on the screen.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last save/run (history trim, unsaved key, plain-HTTP warning).</summary>
    List<string> Warnings { get; }

    /// <summary>
    /// Synchronous one-row read that fills <see cref="HqTransportSelection"/> before the first
    /// OfflineQueueService is constructed, so a till configured for HQ really uses HQ from startup.
    /// Never throws: if the database is not ready the till falls back to the simulated endpoint.
    /// </summary>
    void PrimeTransportSelection();

    /// <summary>Get-or-create the singleton connection row (Id == 1).</summary>
    Task<SyncSetting> GetSettingsAsync();

    /// <summary>
    /// Persist the connection. An empty <paramref name="apiKey"/> means "keep the stored key" — the
    /// screen cannot re-show a secret, so it must never wipe one by leaving the box blank.
    /// </summary>
    Task<(bool Success, string? Error)> SaveSettingsAsync(bool isEnabled, string? baseUrl, string? apiKey,
        string? deviceIdentifier, bool useTlsOnly, string batchSizeLabel, string timeoutLabel);

    /// <summary>Ask the transport that is actually in use whether HQ answers, and report why not.</summary>
    Task<SyncTestResult> TestConnectionAsync();

    /// <summary>One read of the whole picture for the status cards.</summary>
    Task<SyncCenterSnapshot> GetSnapshotAsync(bool probe = true);

    Task<List<SyncRunLog>> GetHistoryAsync(int take = 50);

    /// <summary>Replay the queue now (the queue decides offline gating) and record the run.</summary>
    Task<OfflineSyncOutcome> RunNowAsync(string trigger = "Manual");

    /// <summary>Grant failed rows a fresh attempt budget and replay, recording the run.</summary>
    Task<OfflineSyncOutcome> RetryFailedAsync();

    /// <summary>Drop the recorded history. The change queue itself is untouched.</summary>
    Task<(bool Success, int RemovedCount, string? Error)> ClearHistoryAsync();

    /// <summary>Plain-language reading of the effective configuration: transport, address, queue switches.</summary>
    Task<string> DescribeEffectiveConfigurationAsync();
}

/// <summary>SEC-08: verdict of a Test connection press, with the reason an operator can act on.</summary>
public record SyncTestResult(bool Success, string Message);

/// <summary>
/// SEC-08: everything the control centre shows, in one read. Built by the service so the screen
/// never has to guess which transport is live or whether a probe was attempted.
/// </summary>
public class SyncCenterSnapshot
{
    public SyncSetting Settings { get; set; } = new() { Id = 1 };

    public OfflineQueueStats Queue { get; set; } = new();

    /// <summary>Queue rows per document type (every vocabulary key present, zeroed).</summary>
    public Dictionary<string, int> CountsByEntityType { get; set; } = new();

    /// <summary>Queue rows per status, straight from the queue service.</summary>
    public Dictionary<string, int> CountsByStatus { get; set; } = new();

    /// <summary>True when a probe was made and the endpoint answered; false when it did not or was skipped.</summary>
    public bool IsOnline { get; set; }

    /// <summary>True when a probe was made at all — a screen opened with probing off leaves this false.</summary>
    public bool WasProbed { get; set; }

    /// <summary>Which transport the queue currently has.</summary>
    public string TransportName { get; set; } = string.Empty;

    public bool IsLiveTransport { get; set; }

    /// <summary>Why this transport is in use / what would change it. Honest, never a silent swap.</summary>
    public string TransportReason { get; set; } = string.Empty;

    /// <summary>Reason of the last probe verdict (for live: the HTTP detail; for simulated: the local reason).</summary>
    public string ProbeDetail { get; set; } = string.Empty;

    /// <summary>Headline for the connection card, e.g. "Live HQ endpoint reachable".</summary>
    public string HealthText { get; set; } = string.Empty;

    /// <summary>Effective configuration in one paragraph.</summary>
    public string ConfigurationText { get; set; } = string.Empty;

    /// <summary>Latest recorded run; null when the till has never synced.</summary>
    public SyncRunLog? LastRun { get; set; }

    /// <summary>False when there is no run log and no delivered row — the honest empty state.</summary>
    public bool HasEverSynced { get; set; }

    /// <summary>Rows whose attempt ceiling is exhausted right now (the queue's Failed count).</summary>
    public int BlockingFailureCount => Queue.Failed;

    /// <summary>Queue depth phrased for a card.</summary>
    public string QueueDepthText => Queue.Total == 0
        ? "The local queue holds nothing yet — changes appear here the moment a document is saved."
        : $"{Queue.Total} row(s) recorded, {Queue.PendingCount} still waiting (oldest {Queue.OldestPendingAge}).";

    /// <summary>Document-type breakdown, one line per type, for the queue card.</summary>
    public List<string> EntityBreakdownLines
    {
        get
        {
            var lines = new List<string>();
            foreach (var pair in CountsByEntityType.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
                lines.Add($"{pair.Key}: {pair.Value}");
            return lines;
        }
    }

    /// <summary>Status breakdown, one line per status, for the queue card.</summary>
    public List<string> StatusBreakdownLines
    {
        get
        {
            var lines = new List<string>();
            foreach (var pair in CountsByStatus)
                lines.Add($"{pair.Key}: {pair.Value}");
            return lines;
        }
    }
}
