using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// PAY-08: singleton configuration row (only Id == 1 is used) holding the automatic
/// payment-reminder rule set the reminder engine applies to open receivables.
/// </summary>
public class ReminderSetting
{
    [Key]
    public int Id { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>Days before the due date at which the first notice becomes due.</summary>
    public int FirstNoticeDaysBeforeDue { get; set; } = 3;

    /// <summary>Days past the due date at which the second notice is raised.</summary>
    public int SecondNoticeDaysAfterDue { get; set; } = 7;

    /// <summary>"WhatsApp" / "SMS" / "Email" — the channel every reminder goes out on.</summary>
    [Required]
    [MaxLength(20)]
    public string Channel { get; set; } = "WhatsApp";

    /// <summary>Placeholders: {Customer} {InvoiceNumber} {Amount} {DueDate} {Days} {Company}.</summary>
    [Required]
    [MaxLength(500)]
    public string MessageTemplate { get; set; } = DefaultTemplate;

    /// <summary>Hard cap on reminder messages per customer per calendar day.</summary>
    public int MaxPerCustomerPerDay { get; set; } = 1;

    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// PAY-08: whether the backup scheduler's one-minute tick may fire the reminder run
    /// automatically (manual sends from the reminder screen always stay available).
    /// </summary>
    public bool AutoRunEnabled { get; set; }

    /// <summary>Hour of day (0-23) at which the automatic run fires once a tick passes it.</summary>
    public int AutoRunHour { get; set; } = 9;

    /// <summary>Minute of the hour (0-59) at which the automatic run fires.</summary>
    public int AutoRunMinute { get; set; } = 0;

    /// <summary>Date the automatic run last fired; drives the once-per-day guard in the service.</summary>
    public DateTime? LastAutoRunAt { get; set; }

    /// <summary>Human-readable outcome of the last automatic run (summary or the skip/error reason).</summary>
    [MaxLength(300)]
    public string? LastAutoResult { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    [NotMapped]
    public string RuleSummary => Describe(this);

    [NotMapped]
    public bool IsSecondNoticeEnabled => SecondNoticeDaysAfterDue > 0;

    public const string DefaultTemplate =
        "Namaste {Customer}! Invoice {InvoiceNumber} of {Amount} is {Days}. Due on {DueDate}. Kindly pay at your convenience. - {Company}";

    /// <summary>Plain-language reading of the rule set, shared by the editor and its preview.</summary>
    public static string Describe(ReminderSetting setting)
    {
        if (!setting.IsEnabled)
            return "Automatic reminders are OFF: nothing is queued or sent, the candidate list is informational only.";

        var first = setting.FirstNoticeDaysBeforeDue == 0
            ? "the first notice goes out on the due date itself"
            : $"the first notice goes out {setting.FirstNoticeDaysBeforeDue} day(s) before the due date";
        var second = setting.IsSecondNoticeEnabled
            ? $"a second notice {setting.SecondNoticeDaysAfterDue} day(s) after it"
            : "no second notice is configured";
        var cap = setting.MaxPerCustomerPerDay == 1
            ? "at most 1 message per customer per day"
            : $"at most {setting.MaxPerCustomerPerDay} messages per customer per day";

        return $"Automatic reminders are ON over {setting.Channel}: {first} and {second}, {cap}." +
               (setting.AutoRunEnabled
                   ? $" The scheduler fires it automatically every day at {Math.Clamp(setting.AutoRunHour, 0, 23):00}:{Math.Clamp(setting.AutoRunMinute, 0, 59):00}."
                   : " The scheduler does not fire it automatically; sends happen from this screen.");
    }
}
