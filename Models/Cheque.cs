using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// PAY-11: one instrument in the cheque register — a post-dated cheque received from a customer
/// (Direction "In") or paid out to a supplier (Direction "Out") — plus what happened to it:
/// presented, cleared, bounced or cancelled.
///
/// The register is deliberately independent of <see cref="Payment"/>: a cheque is a promise that
/// may never become money. The link is the other way round — when an inbound cheque CLEARS, the
/// service can raise the receipt through IPaymentService (PAY-03) and store the resulting
/// <see cref="PaymentId"/> / <see cref="PaymentNumber"/> here so the two documents agree.
///
/// Vocabulary fields are plain strings with <c>const</c> + <c>static List&lt;string&gt;</c> options,
/// exactly like <see cref="PendingChange"/> and <see cref="BackupScheduleSetting"/>: SQLite stores
/// them as TEXT, they stay readable in exported CSV, and no EF value converter is needed.
/// </summary>
public class Cheque
{
    [Key]
    public int Id { get; set; }

    /// <summary>In = received from a customer, Out = paid to a supplier — same vocabulary as Payment.Direction.</summary>
    [Required]
    [MaxLength(50)]
    public string Direction { get; set; } = DirectionIn;

    /// <summary>Customer / Supplier — same vocabulary as Payment.PartyType.</summary>
    [Required]
    [MaxLength(50)]
    public string PartyType { get; set; } = PartyCustomer;

    /// <summary>Filled when the party name matches a customer master row (In cheques).</summary>
    public int? CustomerId { get; set; }

    /// <summary>Filled when the party name matches a supplier master row (Out cheques).</summary>
    public int? SupplierId { get; set; }

    /// <summary>Drawer (In) or payee (Out) as written on the leaf — free text, like Payment.PartyName.</summary>
    [Required]
    [MaxLength(200)]
    public string PartyName { get; set; } = string.Empty;

    /// <summary>The bank's printed leaf number as copied off the cheque. NOT an internal series number.</summary>
    [Required]
    [MaxLength(50)]
    public string ChequeNumber { get; set; } = string.Empty;

    /// <summary>Drawer's bank + branch for an In cheque; the shop's own bank + branch for an Out cheque.</summary>
    [MaxLength(200)]
    public string? BankName { get; set; }

    /// <summary>The day the instrument was drawn / received (day precision, kept as UTC midnight).</summary>
    public DateTime IssueDate { get; set; } = DateTime.UtcNow.Date;

    /// <summary>
    /// The date written on the cheque face — the first day it may be presented. For a post-dated
    /// cheque this is in the future, which is the whole point of the register.
    /// </summary>
    public DateTime PresentmentDate { get; set; } = DateTime.UtcNow.Date;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Pending / HandedOver / Cleared / Bounced / Cancelled — one of <see cref="StatusOptions"/>.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = StatusPending;

    /// <summary>Set when the bank honoured it. Day precision, UTC midnight.</summary>
    public DateTime? ClearedDate { get; set; }

    /// <summary>Set when the bank returned it unpaid. Day precision, UTC midnight.</summary>
    public DateTime? BouncedDate { get; set; }

    /// <summary>Why it bounced — the bank's return memo reason (see <see cref="BounceReasonOptions"/>).</summary>
    [MaxLength(300)]
    public string? BounceReason { get; set; }

    /// <summary>
    /// PAY-11 opt-in: when true, marking this inbound cheque as cleared also raises the receipt through
    /// IPaymentService. Off by default so clearing a cheque never moves money by accident.
    /// </summary>
    public bool CreateReceiptOnClear { get; set; }

    /// <summary>Receipt created at clear time (In cheques only); null when no receipt was raised.</summary>
    public int? PaymentId { get; set; }

    /// <summary>Number of that receipt, snapshotted so the grid needs no join.</summary>
    [MaxLength(50)]
    public string? PaymentNumber { get; set; }

    /// <summary>Store that owns the instrument; null = legacy/global, same convention as Payment.StoreId.</summary>
    public int? StoreId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ---- vocabularies shared by the cheque service, the register screen and the CSV export ----

    public const string DirectionIn = "In";
    public const string DirectionOut = "Out";

    public static List<string> DirectionOptions { get; } = new() { DirectionIn, DirectionOut };

    public const string PartyCustomer = "Customer";
    public const string PartySupplier = "Supplier";

    public static List<string> PartyTypeOptions { get; } = new() { PartyCustomer, PartySupplier };

    public const string StatusPending = "Pending";
    public const string StatusHandedOver = "HandedOver";
    public const string StatusCleared = "Cleared";
    public const string StatusBounced = "Bounced";
    public const string StatusCancelled = "Cancelled";

    public static List<string> StatusOptions { get; } =
        new() { StatusPending, StatusHandedOver, StatusCleared, StatusBounced, StatusCancelled };

    /// <summary>Bank return-memo reasons, offered as a picker so a bounce is never recorded blank.</summary>
    public static List<string> BounceReasonOptions { get; } = new()
    {
        "Insufficient funds",
        "Signatures do not match",
        "Account closed",
        "Payment stopped by drawer",
        "Cheque is over the validity period",
        "Date not valid / post-dated presented early",
        "Instrument cancelled",
        "Mutilated cheque",
        "Refer to drawer",
        "Other (see notes)",
    };

    /// <summary>
    /// RBI shortens cheque validity, but shops routinely hold PDCs for a quarter or two of credit
    /// period. A presentment date further out than this is treated as a data-entry slip and blocked.
    /// </summary>
    public const int PdcWindowMonths = 6;

    // ---- [NotMapped] display helpers (the grid binds these instead of raw columns) ----

    [NotMapped]
    public bool IsInbound => Direction == DirectionIn;

    /// <summary>"From Sharma &amp; Sons" (received) / "To Sharma Traders" (paid out).</summary>
    [NotMapped]
    public string PartyLabel => $"{(IsInbound ? "From" : "To")} {PartyName}";

    /// <summary>Plain-language direction, for the register's Type column.</summary>
    [NotMapped]
    public string DirectionLabel => IsInbound ? "Received" : "Paid out";

    /// <summary>True while the instrument still needs a bank outcome.</summary>
    [NotMapped]
    public bool IsOpen => Status is StatusPending or StatusHandedOver;

    /// <summary>True once no further transition is allowed (Cleared / Bounced / Cancelled).</summary>
    [NotMapped]
    public bool IsTerminal => !IsOpen;

    /// <summary>Post-dated: the presentment date has not arrived yet.</summary>
    [NotMapped]
    public bool IsPostDated => PresentmentDate.Date > DateTime.Today;

    /// <summary>Open and its presentment date is today or earlier — the bank column's "present it now" list.</summary>
    [NotMapped]
    public bool IsOverdueToPresent => IsOpen && PresentmentDate.Date <= DateTime.Today;

    /// <summary>Days since the presentment date (0 when still in the future).</summary>
    [NotMapped]
    public int DaysPastPresentment =>
        PresentmentDate.Date >= DateTime.Today ? 0 : (DateTime.Today - PresentmentDate.Date).Days;

    /// <summary>How long the leaf has been in the book, counted from the issue date.</summary>
    [NotMapped]
    public int DaysInBook => Math.Max(0, (DateTime.Today - IssueDate.Date).Days);

    /// <summary>Only a Pending row with no receipt behind it may be deleted.</summary>
    [NotMapped]
    public bool CanDelete => Status == StatusPending && PaymentId == null;

    /// <summary>Terminal rows are history: the edit form is refused so a cleared amount cannot drift.</summary>
    [NotMapped]
    public bool CanEdit => IsOpen;

    /// <summary>True once a receipt has been raised against this cheque (In cheques only).</summary>
    [NotMapped]
    public bool HasLinkedReceipt => PaymentId.HasValue;

    // Day-precision fields are stored as UTC midnight (see the service), so they are shown as-is;
    // CreatedAt/UpdatedAt are real UTC instants and are rendered in local time.

    [NotMapped]
    public string IssueDateText => IssueDate.ToString("dd-MMM-yyyy");

    [NotMapped]
    public string PresentmentDateText => PresentmentDate.ToString("dd-MMM-yyyy");

    [NotMapped]
    public string ClearedDateText => ClearedDate?.ToString("dd-MMM-yyyy") ?? "—";

    [NotMapped]
    public string BouncedDateText => BouncedDate?.ToString("dd-MMM-yyyy") ?? "—";

    [NotMapped]
    public string OutcomeDateText => Status switch
    {
        StatusCleared => ClearedDateText,
        StatusBounced => BouncedDateText,
        _ => "—",
    };

    [NotMapped]
    public string ReceiptLabel => HasLinkedReceipt ? PaymentNumber ?? $"Payment #{PaymentId}" : "no receipt";

    [NotMapped]
    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");

    /// <summary>What the status means, in one sentence — shared by the grid tooltip and the legend.</summary>
    [NotMapped]
    public string StatusHint => Explain(Status);

    [NotMapped]
    public string RowLabel => $"{DirectionLabel} cheque {ChequeNumber} of {Amount:N2} — {PartyLabel}";

    /// <summary>Plain-language reading of a status, including what is allowed next.</summary>
    public static string Explain(string status) => status switch
    {
        StatusHandedOver => "Handed over: the leaf has changed hands but the bank has not given a verdict yet.",
        StatusCleared => "Cleared: the bank paid it. Final — it cannot be bounced or cleared again.",
        StatusBounced => "Bounced: the bank returned it unpaid. Final — reopen the money side on the Payments screen.",
        StatusCancelled => "Cancelled: withdrawn from the register (wrong leaf, stop-payment instruction, lost). Final.",
        _ => "Pending: in the book, waiting for its presentment date and a bank verdict.",
    };
}
