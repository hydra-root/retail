using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// SEC-08: one row per sync run the control centre drove or recorded — when it started, when it
/// finished, how many changes went in, how many HQ took, what happened to the rest and which
/// transport did the work. Bounded by SyncCenterService (oldest rows are trimmed on write) so the
/// history grid stays a shopkeeper's tool rather than an unbounded log file.
/// Nothing in here holds a credential: the API key never reaches this table.
/// </summary>
public class SyncRunLog
{
    [Key]
    public int Id { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null only when the run was interrupted before it could be closed off.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Manual / Auto / Retry failed — who asked for the run.</summary>
    [MaxLength(30)]
    public string Trigger { get; set; } = TriggerManual;

    /// <summary>Name of the transport that ran (simulated or live), exactly as the queue reports it.</summary>
    [MaxLength(120)]
    public string TransportName { get; set; } = string.Empty;

    /// <summary>Changes the run handed to (or offered to) the endpoint.</summary>
    public int AttemptedCount { get; set; }

    public int AcceptedCount { get; set; }

    /// <summary>Rejected this run but still replayable (attempt ceiling not reached).</summary>
    public int RejectedCount { get; set; }

    /// <summary>Rows that hit the attempt ceiling this run and are now Failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Rows left in the queue when the run ended (queued + failed).</summary>
    public int RemainingCount { get; set; }

    /// <summary>One of <see cref="OutcomeOptions"/> — the verdict, so a grid filter needs no parsing.</summary>
    [Required]
    [MaxLength(20)]
    public string Outcome { get; set; } = OutcomeCompleted;

    /// <summary>Why the run did not complete, in the transport's own plain language. Null on a clean run.</summary>
    [MaxLength(500)]
    public string? Error { get; set; }

    /// <summary>Store the run belonged to (the logged-in user's store), so multi-store HQ can tell runs apart.</summary>
    public int? StoreId { get; set; }

    /// <summary>Device name the till presented to HQ. Not a secret, but never a key either.</summary>
    [MaxLength(100)]
    public string? DeviceIdentifier { get; set; }

    // ---- vocabularies ----

    public const string OutcomeCompleted = "Completed";
    public const string OutcomePartial = "Partial";
    public const string OutcomeRejected = "Rejected";
    public const string OutcomeSkipped = "Skipped";
    public const string OutcomeNothingDue = "Nothing due";

    public static List<string> OutcomeOptions { get; } =
        new() { OutcomeCompleted, OutcomePartial, OutcomeRejected, OutcomeSkipped, OutcomeNothingDue };

    public const string TriggerManual = "Manual";
    public const string TriggerAuto = "Auto";
    public const string TriggerRetry = "Retry failed";

    public static List<string> TriggerOptions { get; } = new() { TriggerManual, TriggerAuto, TriggerRetry };

    // ---- [NotMapped] display helpers (the grid binds these instead of raw columns) ----

    [NotMapped]
    public string StartedText => StartedAt.ToLocalTime().ToString("dd-MMM-yyyy HH:mm:ss");

    [NotMapped]
    public string FinishedText =>
        FinishedAt == null ? "still running" : FinishedAt.Value.ToLocalTime().ToString("HH:mm:ss");

    [NotMapped]
    public string DurationText
    {
        get
        {
            if (FinishedAt == null) return "-";
            var ms = (FinishedAt.Value - StartedAt).TotalMilliseconds;
            if (ms < 1000) return $"{(int)Math.Max(0, ms)} ms";
            if (ms < 60_000) return $"{ms / 1000:0.0} s";
            return TimeSpan.FromMilliseconds(ms).ToString(@"h\h mm\m");
        }
    }

    [NotMapped]
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    [NotMapped]
    public bool IsCleanRun => Outcome is OutcomeCompleted or OutcomeNothingDue;

    [NotMapped]
    public string CountText => $"{AcceptedCount} of {AttemptedCount} accepted";

    /// <summary>Store the run came from, in the same words the queue grid uses.</summary>
    [NotMapped]
    public string StoreLabel => StoreId == null ? "All stores" : $"Store #{StoreId}";

    [NotMapped]
    public string DeviceLabel => string.IsNullOrWhiteSpace(DeviceIdentifier) ? "unnamed till" : DeviceIdentifier!;

    [NotMapped]
    public string OutcomeHint => Explain(Outcome);

    /// <summary>Plain-language meaning of an outcome, shared by the grid tooltip and the last-run card.</summary>
    public static string Explain(string outcome) => outcome switch
    {
        OutcomeCompleted => "Completed: the endpoint answered and took every change the run offered.",
        OutcomePartial => "Partial: some changes were accepted and the rest stay queued for the next run.",
        OutcomeRejected => "Rejected: the endpoint answered but took nothing, so each change used one attempt.",
        OutcomeSkipped => "Skipped: the endpoint did not answer, so nothing was sent and no attempts were used.",
        OutcomeNothingDue => "Nothing due: the queue had no change waiting, so there was nothing to send.",
        _ => "Recorded run.",
    };
}
