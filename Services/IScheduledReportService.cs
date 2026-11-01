using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// RPT-13: scheduled report email. Same scheduler singleton pattern as
/// IBackupSchedulerService: a coarse one-minute timer fires once the wall clock
/// passes NextRunAt, the chosen report is built from the same IReportService the
/// report screens use, exported as PDF through IReportExportService and emailed
/// via IMessagingService. Registered as a singleton; the coordinator starts it.
/// </summary>
public interface IScheduledReportService
{
    /// <summary>Loads the singleton settings row, creating it with defaults on first use.</summary>
    Task<ScheduledReportSetting> GetSettingsAsync();

    /// <summary>Persist the schedule, recompute NextRunAt and audit the change.</summary>
    Task<(bool Success, string? Error)> SaveSettingsAsync(ScheduledReportSetting setting);

    /// <summary>Begin the coarse timer. Safe to call more than once; does nothing if already running.</summary>
    void Start();

    /// <summary>Stop the timer. Safe to call when already stopped.</summary>
    void Stop();

    /// <summary>
    /// The schedule decision: build + export + email one report if Now has reached
    /// NextRunAt (missed slots collapse into a single catch-up run) and advance
    /// NextRunAt. Returns true when a report was emailed.
    /// </summary>
    Task<bool> RunIfDueAsync();

    /// <summary>Manual trigger from the view: run the report now, advance the schedule and audit.</summary>
    Task<(bool Success, string Message, string? Path)> RunNowAsync();

    /// <summary>
    /// Next valid slot strictly after <paramref name="from"/> for the given schedule;
    /// handles "today's slot already passed" and the weekday restriction. Public so the
    /// view model can preview it.
    /// </summary>
    DateTime? ComputeNextRun(ScheduledReportSetting setting, DateTime from);

    /// <summary>Builds the export sheet for a report key (RunsNow uses it; public for previews/tests).</summary>
    Task<(ReportExportSheet? Sheet, string? Error)> BuildSheetForReportAsync(
        ScheduledReportSetting setting, DateTime from);

    /// <summary>True while the timer is armed.</summary>
    bool IsRunning { get; }

    /// <summary>Cached view persisted rows for the status card (refreshed by the mutating calls).</summary>
    DateTime? LastRunAt { get; }

    DateTime? NextRunAt { get; }

    string? LastResult { get; }
}
