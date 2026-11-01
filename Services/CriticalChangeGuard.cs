using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-05: guard for critical changes (closed periods and deletes).
///
/// Design notes
/// - The passcode is a shared shop secret, hashed with the SEC-04 <see cref="PasswordHasher" />
///   (PBKDF2-SHA256, 210k iterations, per-hash random salt). Only the hash is ever written;
///   the entered value is used for one comparison and then dropped — it is never returned in a
///   message, never put in an exception and never included in an audit payload.
/// - Attempts/lockout mirror the USR-04 login rule, but the counter is on THIS row so a
///   failed delete attempt cannot lock a user out of the app.
/// - Fail closed: when the master switch is on but no passcode has been stored, gated
///   actions are refused with instructions instead of silently allowed.
/// - Every check is audited, allow as well as refuse (ACC-10 AuditService), with a
///   before/after snapshot of the counters. The snapshot excludes PasscodeHash on purpose.
/// - This complements INV-10 and never replaces it: INV-10 still needs an admin
///   username/password; the SEC-05 passcode is asked for in addition, before the service call.
/// </summary>
public class CriticalChangeGuard : ICriticalChangeGuard
{
    private const string EntityName = "CriticalChangeSetting";

    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;

    public CriticalChangeGuard(AppDbContext dbContext, IAuthService authService, IAuditService auditService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
    }

    public List<CriticalChangeKind> GateOptions => CriticalChangeSetting.GateOptions.ToList();
    public List<int> AttemptLimitOptions => CriticalChangeSetting.AttemptLimitOptions;
    public List<int> LockoutMinutesOptions => CriticalChangeSetting.LockoutMinutesOptions;

    public string DescribeGate(CriticalChangeKind kind) => CriticalChangeSetting.DescribeGate(kind);

    // ---- read side ----

    public async Task<CriticalChangeSetting> GetSettingsAsync()
    {
        var settings = await _dbContext.CriticalChangeSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new CriticalChangeSetting { Id = 1 };
            _dbContext.CriticalChangeSettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<bool> IsEnabledAsync() => (await GetSettingsAsync()).IsEnabled;

    public async Task<bool> IsRequiredAsync(CriticalChangeKind kind) => (await GetSettingsAsync()).Requires(kind);

    public async Task<string> LockoutStateTextAsync()
    {
        var s = await GetSettingsAsync();
        if (s.IsLockedOut)
            return $"Locked until {s.LockoutUntil!.Value.ToLocalTime():dd-MMM-yyyy HH:mm}";

        var attempts = s.FailedAttempts;
        var limit = Math.Max(1, s.MaxFailedAttempts);
        return attempts == 0
            ? $"No failed attempts recorded (limit {limit}, lockout {Math.Max(1, s.LockoutMinutes)} min)."
            : $"{attempts} of {limit} failed attempt(s) used — {Math.Max(0, limit - attempts)} left.";
    }

    // ---- verification ----

    public async Task<GuardOutcome> VerifyAsync(CriticalChangeKind kind, string? passcode, string? reason, string? target = null)
    {
        var settings = await GetSettingsAsync();
        var trimmedReason = Trim(reason, 500);
        var note = string.IsNullOrWhiteSpace(trimmedReason) ? null : trimmedReason;

        // 1. Nothing guarded → let it through without a prompt.
        if (!settings.Requires(kind))
            return GuardOutcome.Allow(
                $"{CriticalChangeSetting.Label(kind)} is not gated by the current SEC-05 policy.", note);

        // 2. Fail closed: gate armed but no secret stored yet.
        if (string.IsNullOrWhiteSpace(settings.PasscodeHash))
            return GuardOutcome.Refuse(
                $"The SEC-05 passcode is switched on for {CriticalChangeSetting.Label(kind).ToLowerInvariant()} " +
                "but has never been set. An administrator must set it under Critical Change Passcode first.", 0, note);

        var storedHash = settings.PasscodeHash;   // non-null from here on (checked above)

        // 3. Already locked out? Do not even hash the candidate.
        if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.UtcNow)
            return GuardOutcome.LockOut(
                $"Too many wrong entries. The critical-change passcode is locked for about " +
                $"{Math.Max(1, (int)Math.Ceiling((settings.LockoutUntil.Value - DateTime.UtcNow).TotalMinutes))} more minute(s).",
                settings.LockoutUntil.Value, note);

        // A finished lockout window clears itself on the next attempt.
        var wasLockedOut = settings.LockoutUntil.HasValue;
        if (wasLockedOut)
        {
            settings.LockoutUntil = null;
            settings.FailedAttempts = 0;
        }

        if (string.IsNullOrWhiteSpace(passcode))
            return GuardOutcome.Refuse("Enter the critical-change passcode to continue.",
                RemainingAttempts(settings, settings.FailedAttempts), note);

        var before = Snapshot(settings);
        var correct = PasswordHasher.Verify(passcode, storedHash);

        if (correct)
        {
            // Verification is not a configuration change, so LastChangedAt/By stay untouched;
            // only the attempt counters move.
            settings.FailedAttempts = 0;
            settings.LockoutUntil = null;
            await SaveAsync(settings);
            await AuditAsync("CriticalChangeAllowed", settings, before,
                $"{CriticalChangeSetting.Label(kind)}{(string.IsNullOrWhiteSpace(target) ? "" : " — " + Trim(target, 120))}",
                $"passcode accepted{(wasLockedOut ? "; expired lockout cleared" : "")}", note);

            return GuardOutcome.Allow(
                $"Critical-change passcode accepted for {CriticalChangeSetting.Label(kind).ToLowerInvariant()}.", note);
        }

        // Wrong secret: count it, and lock the gate when the budget runs out.
        settings.FailedAttempts++;
        var limit = Math.Max(1, settings.MaxFailedAttempts);
        var minutes = Math.Max(1, settings.LockoutMinutes);

        if (settings.FailedAttempts >= limit)
        {
            settings.LockoutUntil = DateTime.UtcNow.AddMinutes(minutes);
            settings.FailedAttempts = 0;
            await SaveAsync(settings);
            await AuditAsync("CriticalChangeLocked", settings, before,
                $"{CriticalChangeSetting.Label(kind)} — locked after {limit} wrong attempt(s)",
                $"gate locked for {minutes} min", note);

            return GuardOutcome.LockOut(
                $"{limit} wrong critical-change passcodes in a row. The gate is locked for {minutes} minutes.",
                settings.LockoutUntil.Value, note);
        }

        await SaveAsync(settings);
        var remaining = RemainingAttempts(settings, settings.FailedAttempts);
        await AuditAsync("CriticalChangeRefused", settings, before,
            CriticalChangeSetting.Label(kind),
            $"wrong passcode (attempt {settings.FailedAttempts} of {limit})", note);

        return GuardOutcome.Refuse(
            $"Incorrect critical-change passcode. {remaining} attempt(s) left before a {minutes}-minute lockout.",
            remaining, note);
    }

    /// <summary>
    /// Hash comparison only — deliberately writes nothing, so the settings screen can offer a
    /// "Test passcode" button that cannot burn attempts, trip a lockout or spam the audit trail.
    /// </summary>
    public async Task<(bool Passed, string? Message)> TestPasscodeAsync(string? passcode)
    {
        var settings = await GetSettingsAsync();

        if (string.IsNullOrWhiteSpace(settings.PasscodeHash))
            return (false, "No passcode is stored yet — set one before testing.");

        if (string.IsNullOrWhiteSpace(passcode))
            return (false, "Type the passcode you want to test.");

        var storedHash = settings.PasscodeHash;   // non-null (checked above)
        var ok = PasswordHasher.Verify(passcode, storedHash);
        return ok
            ? (true, "Correct passcode — a gated action would be allowed" +
                     (settings.IsLockedOut ? " (note: the gate is currently locked, so it would still be refused until the lockout ends)." : "."))
            : (false, "Incorrect passcode — a gated action would be refused.");
    }

    // ---- configuration ----

    public async Task<(bool Success, string? Error)> SetPasscodeAsync(string? newPasscode, string? currentPasscode)
    {
        var settings = await GetSettingsAsync();
        var before = Snapshot(settings);

        if (string.IsNullOrWhiteSpace(newPasscode))
            return (false, "A new passcode is required.");
        if (newPasscode.Length < CriticalChangeSetting.PasscodeMinLength)
            return (false, $"The passcode must be at least {CriticalChangeSetting.PasscodeMinLength} characters.");
        if (newPasscode.Trim().Length != newPasscode.Length)
            return (false, "The passcode cannot start or end with a space.");

        if (!CurrentPasscodeOk(settings, currentPasscode, out var currentError))
            return (false, currentError);

        settings.PasscodeHash = PasswordHasher.Hash(newPasscode);
        settings.FailedAttempts = 0;
        settings.LockoutUntil = null;
        Touch(settings);
        await SaveAsync(settings);

        await AuditAsync("Update", settings, before, "Critical-change passcode changed",
            $"hash replaced; length {newPasscode.Length} characters (the passcode itself is never stored)", null);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ClearPasscodeAsync(string? currentPasscode)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.PasscodeHash))
            return (false, "There is no critical-change passcode to remove.");

        if (!CurrentPasscodeOk(settings, currentPasscode, out var currentError))
            return (false, currentError);

        var before = Snapshot(settings);
        settings.PasscodeHash = null;
        settings.FailedAttempts = 0;
        settings.LockoutUntil = null;
        Touch(settings);
        await SaveAsync(settings);

        await AuditAsync("Update", settings, before, "Critical-change passcode cleared",
            "hash removed — armed gates now fail closed until a new passcode is set", null);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SavePolicyAsync(
        bool isEnabled,
        bool requireForInvoiceDelete,
        bool requireForWriteOff,
        bool requireForEstimateConversion,
        bool requireForLockedPeriodEdit,
        bool requireForStockAdjustment,
        bool requireForFinancialYearLock,
        int maxFailedAttempts,
        int lockoutMinutes)
    {
        if (maxFailedAttempts < 1 || maxFailedAttempts > 20)
            return (false, "The failed-attempt limit must be between 1 and 20.");
        if (lockoutMinutes < 1 || lockoutMinutes > 240)
            return (false, "The lockout must be between 1 and 240 minutes.");

        var settings = await GetSettingsAsync();
        var before = Snapshot(settings);

        // Turning the master switch on without a secret would only lock everyone out of these actions.
        if (isEnabled && string.IsNullOrWhiteSpace(settings.PasscodeHash))
            return (false, "Set the critical-change passcode before enabling the gates.");

        var anyGate = requireForInvoiceDelete || requireForWriteOff || requireForEstimateConversion ||
                      requireForLockedPeriodEdit || requireForStockAdjustment || requireForFinancialYearLock;
        if (isEnabled && !anyGate)
            return (false, "Enable at least one gate, or switch the passcode off.");

        settings.IsEnabled = isEnabled;
        settings.RequireForInvoiceDelete = requireForInvoiceDelete;
        settings.RequireForWriteOff = requireForWriteOff;
        settings.RequireForEstimateConversion = requireForEstimateConversion;
        settings.RequireForLockedPeriodEdit = requireForLockedPeriodEdit;
        settings.RequireForStockAdjustment = requireForStockAdjustment;
        settings.RequireForFinancialYearLock = requireForFinancialYearLock;
        settings.MaxFailedAttempts = maxFailedAttempts;
        settings.LockoutMinutes = lockoutMinutes;
        Touch(settings);
        await SaveAsync(settings);

        await AuditAsync("Update", settings, before, "Critical-change policy",
            "master switch / gate flags / lockout budget", null);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ResetLockoutAsync()
    {
        var settings = await GetSettingsAsync();
        if (settings.FailedAttempts == 0 && !settings.LockoutUntil.HasValue)
            return (false, "The critical-change gate is not locked and has no failed attempts to clear.");

        var before = Snapshot(settings);
        settings.FailedAttempts = 0;
        settings.LockoutUntil = null;
        Touch(settings);
        await SaveAsync(settings);

        await AuditAsync("Update", settings, before, "Critical-change lockout reset",
            "failed attempts cleared, gate reopened", null);
        return (true, null);
    }

    // ---- helpers ----

    /// <summary>When a passcode already exists the caller must prove it; a fresh setup need not.</summary>
    private static bool CurrentPasscodeOk(CriticalChangeSetting settings, string? currentPasscode, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(settings.PasscodeHash)) return true;

        if (string.IsNullOrWhiteSpace(currentPasscode))
        {
            error = "Enter the current critical-change passcode to change or remove it.";
            return false;
        }

        // Read-only check: managing the secret is an admin action, not a gated action,
        // so it must not consume the VerifyAsync attempt budget.
        var ok = PasswordHasher.Verify(currentPasscode, settings.PasscodeHash);
        if (!ok) error = "The current critical-change passcode is incorrect.";
        return ok;
    }

    private static int RemainingAttempts(CriticalChangeSetting settings, int failedAttempts) =>
        Math.Max(0, Math.Max(1, settings.MaxFailedAttempts) - failedAttempts);

    private void Touch(CriticalChangeSetting settings)
    {
        settings.LastChangedAt = DateTime.UtcNow;
        settings.LastChangedBy = _authService.CurrentUser?.Username;
    }

    private async Task SaveAsync(CriticalChangeSetting settings)
    {
        _dbContext.CriticalChangeSettings.Update(settings);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Audit projection of the row WITHOUT the secret material: the hash column is never
    /// copied into ChangesJson/SnapshotJson, only a "passcode is set" flag.
    /// </summary>
    private static object Snapshot(CriticalChangeSetting s) => new
    {
        s.Id,
        s.IsEnabled,
        s.RequireForInvoiceDelete,
        s.RequireForWriteOff,
        s.RequireForEstimateConversion,
        s.RequireForLockedPeriodEdit,
        s.RequireForStockAdjustment,
        s.RequireForFinancialYearLock,
        s.MaxFailedAttempts,
        s.LockoutMinutes,
        s.FailedAttempts,
        s.LockoutUntil,
        PasscodeIsSet = !string.IsNullOrWhiteSpace(s.PasscodeHash),
        s.LastChangedAt,
        s.LastChangedBy,
    };

    private async Task AuditAsync(string action, CriticalChangeSetting settings, object before,
        string label, string detail, string? reason)
    {
        var after = Snapshot(settings);
        var entityLabel = Trim($"{label} | {detail}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" | reason: {reason}"), 200);
        await _auditService.RecordAsync(action, EntityName, settings.Id == 0 ? 1 : settings.Id, entityLabel, before, after);
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
