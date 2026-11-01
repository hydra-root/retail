using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-11: singleton configuration row (only Id == 1 is used) holding the offline / sync
/// switches the change queue consults on every run, plus the outcome of the last run.
/// </summary>
public class OfflineSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Operator-controlled outage: when true nothing leaves the till, even on a working network.</summary>
    public bool IsForcedOffline { get; set; }

    /// <summary>When true a sync is due AutoSyncMinutes after the last one (see IOfflineQueueService.RunAutoSyncAsync).</summary>
    public bool AutoSyncEnabled { get; set; } = true;

    /// <summary>Minutes between automatic replays; one of the <see cref="AutoSyncIntervalOptions"/> labels in the UI.</summary>
    public int AutoSyncMinutes { get; set; } = 15;

    /// <summary>How many times one row may be handed to the endpoint before it is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>UTC stamp of the last run that actually reached the endpoint (a skipped offline run leaves it alone).</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Human-readable outcome of the last run (accepted count, or why it did not sync).</summary>
    [MaxLength(500)]
    public string? LastResult { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    // ---- option vocabularies shared by the service, the editor and the preview ----

    /// <summary>Interval labels the editor binds; each maps 1:1 onto AutoSyncMinutes.</summary>
    public static List<string> AutoSyncIntervalOptions { get; } = new()
    {
        "Every 5 minutes", "Every 15 minutes", "Every 30 minutes",
        "Every 1 hour", "Every 3 hours", "Every 6 hours"
    };

    /// <summary>Attempt-ceiling labels the editor binds; each maps 1:1 onto MaxAttempts.</summary>
    public static List<string> MaxAttemptOptions { get; } = new()
    {
        "3 attempts", "5 attempts", "10 attempts", "20 attempts"
    };

    /// <summary>Default interval when a stored/entered label is not in the vocabulary.</summary>
    public const int DefaultAutoSyncMinutes = 15;

    /// <summary>Default ceiling when a stored/entered label is not in the vocabulary.</summary>
    public const int DefaultMaxAttempts = 5;

    public static int MinutesForInterval(string intervalLabel) => intervalLabel switch
    {
        "Every 5 minutes" => 5,
        "Every 30 minutes" => 30,
        "Every 1 hour" => 60,
        "Every 3 hours" => 180,
        "Every 6 hours" => 360,
        _ => DefaultAutoSyncMinutes,
    };

    /// <summary>Closest vocabulary label for a stored minute count, so the ComboBox always matches an item.</summary>
    public static string IntervalLabel(int autoSyncMinutes) =>
        AutoSyncIntervalOptions.MinBy(option =>
            Math.Abs(MinutesForInterval(option) - Math.Max(1, autoSyncMinutes))) ?? AutoSyncIntervalOptions[0];

    /// <summary>Leading number of an attempt label ("10 attempts" → 10); unknown labels fall back to the default.</summary>
    public static int AttemptsFor(string maxAttemptsLabel) =>
        int.TryParse(maxAttemptsLabel.Split(' ')[0], out var attempts)
            ? Math.Clamp(attempts, 1, 50)
            : DefaultMaxAttempts;

    /// <summary>Closest vocabulary label for a stored ceiling, so the ComboBox always matches an item.</summary>
    public static string AttemptLabel(int maxAttempts) =>
        MaxAttemptOptions.MinBy(option =>
            Math.Abs(AttemptsFor(option) - Math.Max(1, maxAttempts))) ?? MaxAttemptOptions[0];

    [NotMapped]
    public string SyncSummary => Describe(this);

    /// <summary>
    /// Plain-language reading of the switches, built from raw values so the editor can preview
    /// an unsaved configuration exactly as the queue would behave.
    /// </summary>
    public static string Describe(OfflineSetting setting)
    {
        var head = setting.IsForcedOffline
            ? "Forced offline is ON: every change stays in the local queue and nothing is sent, even on a working network."
            : "Forced offline is OFF: the queue replays as soon as the HQ endpoint answers.";

        var auto = setting.AutoSyncEnabled
            ? $"A run is due {IntervalLabel(setting.AutoSyncMinutes).ToLowerInvariant()} after the last one, and each change gets up to {Math.Max(1, setting.MaxAttempts)} attempt(s) before it is marked Failed."
            : "Auto-sync is OFF: replay happens only when an operator presses Sync Now.";

        return $"{head} {auto}";
    }
}
