using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// SEC-01: singleton configuration row (only Id == 1 is used) that drives the
/// automatic backup schedule layered on top of the manual BackupService.
/// </summary>
public class BackupScheduleSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Master switch: when false the scheduler never runs.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>One of <see cref="FrequencyOptions"/>: Daily / Weekly / "Every N days".</summary>
    [Required]
    [MaxLength(20)]
    public string Frequency { get; set; } = FrequencyDaily;

    /// <summary>Gap in days for the "Every N days" frequency; ignored otherwise.</summary>
    public int IntervalDays { get; set; } = 1;

    /// <summary>Weekday name for Weekly (e.g. "Sunday"); ignored otherwise.</summary>
    [MaxLength(12)]
    public string DayOfWeek { get; set; } = "Sunday";

    /// <summary>Hour of day (0-23) the schedule fires; kept as a plain int so the UI can bind it directly.</summary>
    public int RunAtHour { get; set; } = 21;

    /// <summary>Minute of the hour (0-59) the schedule fires.</summary>
    public int RunAtMinute { get; set; } = 30;

    /// <summary>Folder automatic copies land in; null means IBackupService.GetDefaultBackupPath()'s folder.</summary>
    [MaxLength(500)]
    public string? DestinationFolder { get; set; }

    /// <summary>How many automatic copies to retain before pruning the oldest.</summary>
    public int KeepGenerations { get; set; } = 14;

    /// <summary>When false, pruning only ever touches files this scheduler created.</summary>
    public bool IncludeManualBackupsInPrune { get; set; }

    public DateTime? LastRunAt { get; set; }

    /// <summary>Human-readable outcome of the last run (success text or the failure reason).</summary>
    [MaxLength(500)]
    public string? LastResult { get; set; }

    /// <summary>Source of truth for "is a run due": set on save/after each run, the tick fires when Now passes it.</summary>
    public DateTime? NextRunAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    // ---- frequency vocabulary shared by the service, editor and preview ----

    public const string FrequencyDaily = "Daily";
    public const string FrequencyWeekly = "Weekly";
    /// <summary>Category label for the interval frequency; the actual N lives in <see cref="IntervalDays"/>.</summary>
    public const string FrequencyEveryN = "Every N days";

    public static List<string> FrequencyOptions { get; } =
        new() { FrequencyDaily, FrequencyWeekly, FrequencyEveryN };

    /// <summary>Weekday names for the Weekly option (Sunday first, matching System.DayOfWeek).</summary>
    public static List<string> DayOfWeekOptions { get; } =
        System.Enum.GetNames(typeof(System.DayOfWeek)).ToList();

    /// <summary>
    /// Prefix that marks a backup file as scheduler-produced so pruning can tell
    /// its own copies apart from the manual ones BackupService names "backup_*.db".
    /// </summary>
    public const string AutoFilePrefix = "AutoBackup_";

    /// <summary>Plain-language schedule, e.g. "Weekly on Sunday at 21:30, keep 14 copies".</summary>
    [NotMapped]
    public string ScheduleSummary => Describe(Frequency, IntervalDays, DayOfWeek, RunAtHour, RunAtMinute, KeepGenerations);

    /// <summary>
    /// Builds the human-readable summary from raw field values, so the editor can
    /// preview an unsaved schedule by passing its current (not yet persisted) numbers.
    /// </summary>
    public static string Describe(string frequency, int intervalDays, string? dayOfWeek,
        int hour, int minute, int keepGenerations)
    {
        var time = $"{System.Math.Clamp(hour, 0, 23):00}:{System.Math.Clamp(minute, 0, 59):00}";
        var keep = Math.Max(1, keepGenerations);
        var keepText = $"keep {keep} {(keep == 1 ? "copy" : "copies")}";

        var head = frequency switch
        {
            FrequencyWeekly => $"Weekly on {(string.IsNullOrWhiteSpace(dayOfWeek) ? "Sunday" : dayOfWeek)} at {time}",
            FrequencyEveryN => $"Every {Math.Max(1, intervalDays)} days at {time}",
            _ => $"Daily at {time}",
        };

        return $"{head}, {keepText}";
    }
}
