using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// RPT-13: singleton configuration row (only Id == 1 is used) that drives the
/// scheduled report email — the same singleton-row + status-card idiom as
/// <see cref="BackupScheduleSetting"/>, but with a simpler schedule slot:
/// a daily time-of-day plus an optional weekday restriction (empty
/// <see cref="DayOfWeek"/> = every day, a weekday name = only that day).
/// </summary>
public class ScheduledReportSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Master switch: when false the scheduler never runs.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Which report is emailed; "" = none configured. One of <see cref="ReportKeyOptions"/>.</summary>
    [MaxLength(40)]
    public string ReportKey { get; set; } = string.Empty;

    /// <summary>Display title of the chosen report (kept for the audit trail and the email body).</summary>
    [MaxLength(100)]
    public string ReportTitle { get; set; } = string.Empty;

    /// <summary>Recipient of the report email (SMTP "to"; may be a comma-separated list).</summary>
    [MaxLength(320)]
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Subject line of the report email.</summary>
    [MaxLength(200)]
    public string Subject { get; set; } = "Daily sales report";

    /// <summary>Hour of day (0-23) the report fires; kept as a plain int so the UI can bind it directly.</summary>
    public int RunAtHour { get; set; } = 8;

    /// <summary>Minute of the hour (0-59) the report fires.</summary>
    public int RunAtMinute { get; set; } = 0;

    /// <summary>
    /// Weekday name ("Monday" … "Sunday") to restrict the run to that day only,
    /// or <see cref="DayOfWeekDaily"/> ("") for every day.
    /// </summary>
    [MaxLength(12)]
    public string DayOfWeek { get; set; } = "Monday";

    public DateTime? LastRunAt { get; set; }

    /// <summary>Human-readable outcome of the last run (success text or the failure reason).</summary>
    [MaxLength(500)]
    public string? LastResult { get; set; }

    /// <summary>Source of truth for "is a run due": set on save/after each run, the tick fires when Now passes it.</summary>
    public DateTime? NextRunAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    // ---- vocabulary shared by the service, the editor and the preview ----

    /// <summary><see cref="DayOfWeek"/> value meaning "run every day".</summary>
    public const string DayOfWeekDaily = "";

    /// <summary>
    /// Picker options for the send day: "Every day" first, then the real weekday names
    /// (System.DayOfWeek order, matching <see cref="BackupScheduleSetting"/>).
    /// </summary>
    public static List<string> DayOfWeekOptions { get; } =
        new() { DayOfWeekDailyLabel }.Concat(
            System.Enum.GetNames(typeof(System.DayOfWeek))).ToList();

    public const string DayOfWeekDailyLabel = "Every day";

    /// <summary>Human keyword for a stored <see cref="DayOfWeek"/> (used by status text).</summary>
    public static string DescribeDay(string? dayOfWeek) =>
        string.IsNullOrWhiteSpace(dayOfWeek) ? DayOfWeekDailyLabel : dayOfWeek.Trim();

    /// <summary>Converts a picker option to the stored value ("Every day" → "").</summary>
    public static string ToStoredDay(string? option) =>
        string.IsNullOrWhiteSpace(option) || option == DayOfWeekDailyLabel
            ? DayOfWeekDaily
            : option.Trim();

    // ---- report picker ----

    public const string ReportKeyDailySales = "DailySales";
    public const string ReportKeyProfitLoss = "ProfitLoss";
    public const string ReportKeyStockAudit = "StockAudit";
    public const string ReportKeyReceivables = "Receivables";
    public const string ReportKeyGst = "Gst";

    /// <summary>Keys the export/scheduler stack can build. "" = none (picker shows "(none)").</summary>
    public static List<string> ReportKeyOptions { get; } = new()
    {
        string.Empty,
        ReportKeyDailySales,
        ReportKeyProfitLoss,
        ReportKeyStockAudit,
        ReportKeyReceivables,
        ReportKeyGst,
    };

    /// <summary>Picker label for a key ("" shows as "(none)"), or the key itself when unknown.</summary>
    public static string DescribeReportKey(string? key) => key switch
    {
        "" or null => "(none)",
        ReportKeyDailySales => "Daily Sales (last 7 days)",
        ReportKeyProfitLoss => "Profit & Loss (last 7 days)",
        ReportKeyStockAudit => "Stock Audit (current snapshot)",
        ReportKeyReceivables => "Receivables (current snapshot)",
        ReportKeyGst => "GST Report (last 7 days)",
        _ => key,
    };

    /// <summary>Display row for the scheduled-report picker (key + label as separate columns).</summary>
    public record ReportKeyChoice(string Key, string Label);

    public static List<ReportKeyChoice> ReportKeyChoices { get; } =
        ReportKeyOptions.Select(k => new ReportKeyChoice(k, DescribeReportKey(k))).ToList();
}
