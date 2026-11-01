using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>SEC-05: verdict of a critical-change passcode check.</summary>
public enum GuardStatus
{
    /// <summary>The action may proceed (passcode correct, or the gate is switched off).</summary>
    Allowed,

    /// <summary>No passcode / wrong passcode — the action must not proceed, another try is possible.</summary>
    Refused,

    /// <summary>Too many wrong attempts: the gate is shut until <see cref="GuardOutcome.LockedUntilUtc"/>.</summary>
    LockedOut
}

/// <summary>
/// SEC-05: immutable result of <see cref="ICriticalChangeGuard.VerifyAsync" />.
/// Carries the human message the caller shows verbatim, plus the lockout instant
/// so the dialog can render a countdown.
/// </summary>
public sealed class GuardOutcome
{
    public GuardStatus Status { get; }
    public string Message { get; }

    /// <summary>Set only for <see cref="GuardStatus.LockedOut" /> — UTC instant the gate reopens.</summary>
    public DateTime? LockedUntilUtc { get; }

    /// <summary>Attempts left before lockout (0 when not applicable).</summary>
    public int RemainingAttempts { get; }

    /// <summary>The reason the operator typed, recorded for the caller's convenience.</summary>
    public string? Reason { get; }

    public bool Allowed => Status == GuardStatus.Allowed;
    public bool Refused => Status == GuardStatus.Refused;
    public bool LockedOut => Status == GuardStatus.LockedOut;

    private GuardOutcome(GuardStatus status, string message, DateTime? lockedUntilUtc, int remainingAttempts, string? reason)
    {
        Status = status;
        Message = message;
        LockedUntilUtc = lockedUntilUtc;
        RemainingAttempts = remainingAttempts;
        Reason = reason;
    }

    public static GuardOutcome Allow(string message, string? reason = null) =>
        new(GuardStatus.Allowed, message, null, 0, reason);

    public static GuardOutcome Refuse(string message, int remainingAttempts = 0, string? reason = null) =>
        new(GuardStatus.Refused, message, null, remainingAttempts, reason);

    public static GuardOutcome LockOut(string message, DateTime untilUtc, string? reason = null) =>
        new(GuardStatus.LockedOut, message, untilUtc, 0, reason);

    public override string ToString() => $"{Status}: {Message}";
}

/// <summary>
/// SEC-05: the separate passcode guard for critical changes (closed periods and deletes).
/// Single source of truth for "may this destructive/period-changing action run without
/// re-proving the shop secret?" — the ViewModels ask, the guard decides and audits both
/// outcomes. It never sees the passcode in clear after VerifyAsync returns, and never
/// stores or logs anything but the PBKDF2 hash.
/// </summary>
public interface ICriticalChangeGuard
{
    /// <summary>Master switch on the singleton row (per-gate flags are not considered here).</summary>
    Task<bool> IsEnabledAsync();

    /// <summary>True when the given kind is guarded right now (master switch AND its own flag).</summary>
    Task<bool> IsRequiredAsync(CriticalChangeKind kind);

    /// <summary>The singleton row, created on first use. Callers must never persist it back themselves.</summary>
    Task<CriticalChangeSetting> GetSettingsAsync();

    /// <summary>
    /// Checks one entry of the passcode for one gated action. Counts attempts, applies the
    /// lockout, and writes an audit record for BOTH allow and refuse.
    /// </summary>
    /// <param name="kind">Which gate is being crossed.</param>
    /// <param name="passcode">What the operator typed (never stored, never logged).</param>
    /// <param name="reason">Optional free-text justification copied into the audit trail.</param>
    /// <param name="target">Optional document label being changed, e.g. "INV-20260101-0004" (audit only).</param>
    Task<GuardOutcome> VerifyAsync(CriticalChangeKind kind, string? passcode, string? reason, string? target = null);

    /// <summary>
    /// Hash comparison only — no attempt counting, no lockout, no audit. Backs the
    /// settings screen's "Test passcode" button so a trial cannot lock anyone out.
    /// </summary>
    Task<(bool Passed, string? Message)> TestPasscodeAsync(string? passcode);

    /// <summary>Sets or replaces the shared passcode. The current one is required once one is stored.</summary>
    Task<(bool Success, string? Error)> SetPasscodeAsync(string? newPasscode, string? currentPasscode);

    /// <summary>Clears the stored hash, e.g. after a staff departure. Requires the current passcode.</summary>
    Task<(bool Success, string? Error)> ClearPasscodeAsync(string? currentPasscode);

    /// <summary>Persists the master switch, the gate flags and the lockout budget.</summary>
    Task<(bool Success, string? Error)> SavePolicyAsync(
        bool isEnabled,
        bool requireForInvoiceDelete,
        bool requireForWriteOff,
        bool requireForEstimateConversion,
        bool requireForLockedPeriodEdit,
        bool requireForStockAdjustment,
        bool requireForFinancialYearLock,
        int maxFailedAttempts,
        int lockoutMinutes);

    /// <summary>Hands the gate back to an operator after a lockout (admin action; audited).</summary>
    Task<(bool Success, string? Error)> ResetLockoutAsync();

    /// <summary>Attempt/lockout state for the settings screen status card.</summary>
    Task<string> LockoutStateTextAsync();

    /// <summary>Gate vocabulary + explanation for the editor and the prompt dialogs.</summary>
    string DescribeGate(CriticalChangeKind kind);

    /// <summary>Display order of the gates (settings screen checklist).</summary>
    List<CriticalChangeKind> GateOptions { get; }

    List<int> AttemptLimitOptions { get; }
    List<int> LockoutMinutesOptions { get; }
}
