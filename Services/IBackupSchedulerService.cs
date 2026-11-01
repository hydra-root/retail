using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-01: runs scheduled automatic backups (Daily / Weekly / Every N days) on top of
/// the existing manual IBackupService, and retains only the newest KeepGenerations copies.
/// Registered as a singleton; the coordinator starts it once the app is up.
/// </summary>
public interface IBackupSchedulerService
{
    Task<BackupScheduleSetting> GetSettingsAsync();

    /// <summary>Persist the schedule, recomputing NextRunAt and auditing the change.</summary>
    Task<(bool Success, string? Error)> SaveSettingsAsync(BackupScheduleSetting setting);

    /// <summary>Begin the coarse timer. Safe to call more than once; does nothing if already running.</summary>
    void Start();

    /// <summary>Stop the timer. Safe to call when already stopped.</summary>
    void Stop();

    /// <summary>
    /// The schedule decision: run one backup if Now has reached NextRunAt (catching up
    /// missed slots in a single run) and advance NextRunAt. Returns true when a backup ran.
    /// Public so the coordinator can call it once on app start.
    /// </summary>
    Task<bool> RunIfDueAsync();

    /// <summary>Manual trigger from the view: run now, prune, advance the schedule and audit.</summary>
    Task<(bool Success, string Message, string? Path)> RunNowAsync();

    /// <summary>
    /// Next valid slot strictly after <paramref name="from"/> for the given schedule;
    /// handles "today's slot already passed". Public so the view model can preview it.
    /// </summary>
    DateTime? ComputeNextRun(BackupScheduleSetting setting, DateTime from);

    /// <summary>True while the timer is armed.</summary>
    bool IsRunning { get; }

    /// <summary>Cached view of the schedule for the status card (refreshed by the mutating calls).</summary>
    DateTime? LastRunAt { get; }

    DateTime? NextRunAt { get; }

    string? LastResult { get; }
}
