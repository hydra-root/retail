using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// SEC-05: the critical operations a separate shop passcode can gate.
/// Each kind maps to one boolean on <see cref="CriticalChangeSetting"/>.
/// </summary>
public enum CriticalChangeKind
{
    /// <summary>Deleting an invoice / estimate from the register.</summary>
    InvoiceDelete,

    /// <summary>Writing off the remaining amount of an invoice (bad debts).</summary>
    WriteOff,

    /// <summary>Converting an estimate into a real invoice (stock moves, number is regenerated).</summary>
    EstimateConversion,

    /// <summary>Changing a document whose date falls in a locked financial period.</summary>
    LockedPeriodEdit,

    /// <summary>Posting a manual stock adjustment.</summary>
    StockAdjustment,

    /// <summary>Locking or unlocking a financial year (closing a period).</summary>
    FinancialYearLock
}

/// <summary>
/// SEC-05: singleton configuration row (only Id == 1 is used) holding the separate
/// "critical change" passcode plus which operations it guards. Complements — never
/// replaces — the INV-10 admin username/password override: the override says
/// "an admin authorised this", this passcode says "the person who knows the shop
/// secret is present". Both are applied, in that order, at each call site.
/// The passcode itself is never stored: only its PBKDF2 hash (SEC-04 PasswordHasher).
/// </summary>
public class CriticalChangeSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Master switch: when false every gate is open and no passcode is asked for.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>PBKDF2 hash of the shared passcode ("PBKDF2$iterations$salt$hash"). Never the passcode itself.</summary>
    [MaxLength(255)]
    public string? PasscodeHash { get; set; }

    // ---- per-gate toggles (each one covers a CriticalChangeKind) ----

    public bool RequireForInvoiceDelete { get; set; } = true;
    public bool RequireForWriteOff { get; set; } = true;

    /// <summary>Estimate to invoice is an everyday billing step, so it ships unarmed.</summary>
    public bool RequireForEstimateConversion { get; set; }

    public bool RequireForLockedPeriodEdit { get; set; } = true;
    public bool RequireForStockAdjustment { get; set; }
    public bool RequireForFinancialYearLock { get; set; } = true;

    // ---- brute-force defence (mirrors USR-04 login lockout) ----

    /// <summary>Wrong passcodes tolerated before the gate locks. Clamped to 1-20 on save.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Minutes the gate stays shut once MaxFailedAttempts is hit. Clamped to 1-240 on save.</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Consecutive wrong entries since the last success or reset.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>UTC instant the lockout ends; null when the gate is not locked.</summary>
    public DateTime? LockoutUntil { get; set; }

    // ---- provenance ----

    public DateTime? LastChangedAt { get; set; }

    [MaxLength(100)]
    public string? LastChangedBy { get; set; }

    // ---- vocabulary shared by the guard, the dialog and the settings screen ----

    /// <summary>Short label for a gate, used in dialogs and on the settings screen.</summary>
    public static string Label(CriticalChangeKind kind) => kind switch
    {
        CriticalChangeKind.InvoiceDelete => "Invoice / estimate delete",
        CriticalChangeKind.WriteOff => "Invoice write-off",
        CriticalChangeKind.EstimateConversion => "Estimate to invoice conversion",
        CriticalChangeKind.LockedPeriodEdit => "Edit inside a locked period",
        CriticalChangeKind.StockAdjustment => "Stock adjustment",
        _ => "Financial year lock / unlock"
    };

    /// <summary>Why this operation is gated — the explanation line in the passcode prompt.</summary>
    public static string DescribeGate(CriticalChangeKind kind) => kind switch
    {
        CriticalChangeKind.InvoiceDelete =>
            "Deleting a billing document erases the GST audit trail permanently, so SEC-05 requires the critical-change passcode.",
        CriticalChangeKind.WriteOff =>
            "A write-off posts a Bad Debts journal entry and closes a receivable without cash, so SEC-05 requires the critical-change passcode.",
        CriticalChangeKind.EstimateConversion =>
            "Converting an estimate turns a quotation into a real GST document, regenerates the invoice number and moves stock, so SEC-05 can require the critical-change passcode.",
        CriticalChangeKind.LockedPeriodEdit =>
            "This document falls in a closed financial period; changing it after closing the books needs the critical-change passcode (SEC-05) on top of the INV-10 admin override.",
        CriticalChangeKind.StockAdjustment =>
            "A manual stock adjustment moves inventory value without a purchase or sale document, so SEC-05 requires the critical-change passcode.",
        _ =>
            "Locking or unlocking a financial period opens or freezes the whole year for posting, so SEC-05 requires the critical-change passcode."
    };

    /// <summary>The policy column that switches a gate on — keeps the guard and the editor honest.</summary>
    public static string FlagName(CriticalChangeKind kind) => kind switch
    {
        CriticalChangeKind.InvoiceDelete => nameof(RequireForInvoiceDelete),
        CriticalChangeKind.WriteOff => nameof(RequireForWriteOff),
        CriticalChangeKind.EstimateConversion => nameof(RequireForEstimateConversion),
        CriticalChangeKind.LockedPeriodEdit => nameof(RequireForLockedPeriodEdit),
        CriticalChangeKind.StockAdjustment => nameof(RequireForStockAdjustment),
        _ => nameof(RequireForFinancialYearLock)
    };

    /// <summary>Every gate in display order (the checklist on the settings screen).</summary>
    public static CriticalChangeKind[] GateOptions { get; } =
    [
        CriticalChangeKind.InvoiceDelete,
        CriticalChangeKind.WriteOff,
        CriticalChangeKind.EstimateConversion,
        CriticalChangeKind.LockedPeriodEdit,
        CriticalChangeKind.StockAdjustment,
        CriticalChangeKind.FinancialYearLock
    ];

    /// <summary>Offered wrong-attempt limits (ComboBox vocabulary).</summary>
    public static List<int> AttemptLimitOptions { get; } = new() { 3, 5, 8, 10 };

    /// <summary>Offered lockout lengths in minutes (ComboBox vocabulary).</summary>
    public static List<int> LockoutMinutesOptions { get; } = new() { 5, 10, 15, 30, 60 };

    /// <summary>Minimum accepted passcode length (matches the USR-04 password rule).</summary>
    public const int PasscodeMinLength = 6;

    [NotMapped]
    public bool HasPasscode => !string.IsNullOrWhiteSpace(PasscodeHash);

    [NotMapped]
    public bool IsLockedOut => LockoutUntil.HasValue && LockoutUntil.Value > DateTime.UtcNow;

    /// <summary>True when this specific kind is guarded by the current policy (master switch included).</summary>
    public bool Requires(CriticalChangeKind kind) => IsEnabled && kind switch
    {
        CriticalChangeKind.InvoiceDelete => RequireForInvoiceDelete,
        CriticalChangeKind.WriteOff => RequireForWriteOff,
        CriticalChangeKind.EstimateConversion => RequireForEstimateConversion,
        CriticalChangeKind.LockedPeriodEdit => RequireForLockedPeriodEdit,
        CriticalChangeKind.StockAdjustment => RequireForStockAdjustment,
        _ => RequireForFinancialYearLock
    };

    /// <summary>Plain sentence naming the gates that are currently armed, e.g. for the settings preview.</summary>
    public string PolicySummary => Summarize(IsEnabled, RequireForInvoiceDelete, RequireForWriteOff,
        RequireForEstimateConversion, RequireForLockedPeriodEdit, RequireForStockAdjustment,
        RequireForFinancialYearLock, MaxFailedAttempts, LockoutMinutes);

    /// <summary>Builds the summary from raw values so the editor can preview an unsaved policy.</summary>
    public static string Summarize(bool isEnabled, bool invoiceDelete, bool writeOff, bool estimateConversion,
        bool lockedPeriodEdit, bool stockAdjustment, bool financialYearLock,
        int maxFailedAttempts, int lockoutMinutes)
    {
        if (!isEnabled)
            return "Critical-change passcode is OFF — deletes, write-offs, estimate conversions, locked-period edits, stock adjustments and period locks run without it.";

        var armed = new List<string>();
        if (invoiceDelete) armed.Add(Label(CriticalChangeKind.InvoiceDelete));
        if (writeOff) armed.Add(Label(CriticalChangeKind.WriteOff));
        if (estimateConversion) armed.Add(Label(CriticalChangeKind.EstimateConversion));
        if (lockedPeriodEdit) armed.Add(Label(CriticalChangeKind.LockedPeriodEdit));
        if (stockAdjustment) armed.Add(Label(CriticalChangeKind.StockAdjustment));
        if (financialYearLock) armed.Add(Label(CriticalChangeKind.FinancialYearLock));

        var gates = armed.Count == 0
            ? "no gate is selected, so nothing is asked for"
            : "gates: " + string.Join(", ", armed);

        return $"Critical-change passcode is ON — {gates}. " +
               $"{Math.Max(1, maxFailedAttempts)} wrong attempt(s) before a {Math.Max(1, lockoutMinutes)}-minute lockout.";
    }
}
