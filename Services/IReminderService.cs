using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-08: automatic payment reminders. Applies the ReminderSetting rule set to the
/// open receivables and pushes the result through the Messaging Center channels.
/// </summary>
public interface IReminderService
{
    /// <summary>Channels a reminder can be sent over (mirrors the Messaging Center).</summary>
    List<string> Channels { get; }

    /// <summary>Stages a candidate can be classified into, in escalation order.</summary>
    List<string> Stages { get; }

    /// <summary>Human-readable reason for the most recent validation failure.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last candidate build / send run.</summary>
    List<string> Errors { get; }

    Task<ReminderSetting> GetSettingsAsync();

    Task<(bool Success, string? Error)> SaveSettingsAsync(ReminderSetting setting);

    /// <summary>
    /// Every open, non-estimate invoice classified against the rule set for <paramref name="asOf"/>.
    /// Rows outside the notice window or missing a channel target come back with
    /// <see cref="ReminderCandidate.Included"/> false and a Reason saying why.
    /// </summary>
    Task<List<ReminderCandidate>> GetCandidatesAsync(DateTime asOf);

    /// <summary>Sends the given candidates over the configured channel, deduped per day.</summary>
    Task<ReminderSendResult> SendAsync(IEnumerable<ReminderCandidate> candidates);

    /// <summary>
    /// PAY-08: automatic run, armed from the backup scheduler's one-minute tick. Fires the
    /// existing candidate-build + send pipeline once per day, when the clock has passed
    /// <see cref="ReminderSetting.AutoRunHour"/>:<see cref="ReminderSetting.AutoRunMinute"/>
    /// and <see cref="ReminderSetting.AutoRunEnabled"/> is on. Returns a short summary (or the
    /// skip reason / "error: …" text) for <see cref="ReminderSetting.LastAutoResult"/>; never throws.
    /// </summary>
    Task<string?> RunAutoRemindersAsync(DateTime now);

    /// <summary>
    /// Renders <see cref="ReminderSetting.MessageTemplate"/> for one candidate. Exposed so the
    /// editor can preview an unsaved template exactly as the engine would send it.
    /// </summary>
    string RenderTemplate(ReminderSetting setting, ReminderCandidate candidate);
}

/// <summary>
/// PAY-08: one open invoice seen through the reminder rule set. Plain DTO — the
/// reminder screen wraps it for selection, so it stays free of INotifyPropertyChanged.
/// </summary>
public class ReminderCandidate
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }

    /// <summary>Null for invoices billed without a customer record — such rows cannot be messaged.</summary>
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Mobile for WhatsApp/SMS, email address for Email; empty when the customer has none.</summary>
    public string Recipient { get; set; } = string.Empty;

    public decimal Outstanding { get; set; }

    /// <summary>Days until the due date when positive, days past it when negative.</summary>
    public int DaysToOrFromDue { get; set; }

    /// <summary>"Upcoming" / "Overdue" / "Second Notice" / "None".</summary>
    public string Stage { get; set; } = ReminderStages.None;

    public string Message { get; set; } = string.Empty;

    /// <summary>False when the row is listed only for explanation and must not be sent.</summary>
    public bool Included { get; set; }

    /// <summary>Why the row is queued, or why it is not.</summary>
    public string Reason { get; set; } = string.Empty;

    public string StageLabel => Stage switch
    {
        ReminderStages.Upcoming => "Upcoming (first notice)",
        ReminderStages.Overdue => "Overdue",
        ReminderStages.SecondNotice => "Second notice",
        _ => "Not in any notice window",
    };

    public string DaysLabel => FormatDays(DaysToOrFromDue);

    /// <summary>"in N days" ahead of the due date, "N days overdue" behind it.</summary>
    public static string FormatDays(int daysToOrFromDue) => daysToOrFromDue switch
    {
        0 => "due today",
        > 1 => $"in {daysToOrFromDue} days",
        1 => "in 1 day",
        -1 => "1 day overdue",
        _ => $"{-daysToOrFromDue} days overdue",
    };
}

/// <summary>PAY-08: stage keys used by the rule engine and the screen's filter.</summary>
public static class ReminderStages
{
    public const string Upcoming = "Upcoming";
    public const string Overdue = "Overdue";
    public const string SecondNotice = "Second Notice";
    public const string None = "None";
}

/// <summary>PAY-08: aggregate of one send run.</summary>
public class ReminderSendResult
{
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }

    public List<ReminderSendRow> Rows { get; } = new();

    public string Summary => $"{Sent} sent, {Skipped} skipped, {Failed} failed.";
}

/// <summary>PAY-08: per-candidate outcome of a send run.</summary>
public class ReminderSendRow
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;

    /// <summary>"Sent" / "Failed" / "Skipped".</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Messaging Center status ("Sent" / "Opened in browser" / "Draft" / "Failed") when attempted.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Reason for a skip, or the provider detail for an attempt.</summary>
    public string Detail { get; set; } = string.Empty;
}
