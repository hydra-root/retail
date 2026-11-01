using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-03: the read/report + one safe write action behind the "Security &amp; Encryption" screen.
/// Everything it returns is value-free — it reports whether a secret exists and how it is stored,
/// never the secret. It is the only place that touches stored secrets for reporting.
/// </summary>
public interface ISecurityService
{
    /// <summary>Which stored secrets exist and whether each is protected at rest (counts/labels only).</summary>
    Task<List<SecretStatusRow>> GetSecretInventoryAsync();

    /// <summary>Honest whole-database-encryption state plus the backup gate for the opt-in action.</summary>
    Task<DatabaseSecurityStatus> GetDatabaseStatusAsync();

    /// <summary>Re-protect every secret whose write path can be upgraded safely today. Returns counts only.</summary>
    Task<SecretProtectionReport> ProtectStoredSecretsAsync();
}

public class SecurityService : ISecurityService
{
    private readonly AppDbContext _dbContext;
    private readonly ISecretProtector _protector;
    private readonly IEncryptionService _encryption;
    private readonly IBackupService _backupService;

    public SecurityService(
        AppDbContext dbContext,
        ISecretProtector protector,
        IEncryptionService encryption,
        IBackupService backupService)
    {
        _dbContext = dbContext;
        _protector = protector;
        _encryption = encryption;
        _backupService = backupService;
    }

    public async Task<List<SecretStatusRow>> GetSecretInventoryAsync()
    {
        var rows = new List<SecretStatusRow>();

        // --- Email (SMTP) password: written & read through the protector, so bulk-protectable. ---
        var messaging = await _dbContext.MessagingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        rows.Add(Describe(
            "Email (SMTP) password",
            "MessagingSetting.SmtpPassword (Id 1)",
            messaging?.SmtpPassword,
            protectable: true,
            protectedNote: "DPAPI-encrypted at rest; only the messaging service holds the plaintext while sending.",
            plaintextNote: "Stored in the clear. 'Protect stored secrets now' will encrypt it with no downtime."));

        // --- HQ sync API key: SEC-08 stores/reads it raw on the transport, so NOT bulk-protectable yet. ---
        var sync = await _dbContext.SyncSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        rows.Add(Describe(
            "HQ sync API key",
            "SyncSetting.ApiKey (Id 1)",
            sync?.ApiKey,
            protectable: false,
            protectedNote: "Protected at rest.",
            plaintextNote: "Stored in the clear, but intentionally NOT auto-encrypted: the live HTTPS transport " +
                           "sends this value verbatim and does not decrypt on read. Encrypting it here would " +
                           "break live sync until the SEC-08 read path is routed through the protector."));

        // --- One-way hashes: reported so the screen is complete, but they are never DPAPI-wrapped. ---
        rows.Add(new SecretStatusRow
        {
            DisplayName = "Login & passcode secrets (hashed)",
            Location = "User.PasswordHash / CriticalChangeSetting.PasscodeHash",
            IsSet = true,
            IsProtectedAtRest = true,
            IsPlaintext = false,
            ProtectableByBulkAction = false,
            StateLabel = "Hashed (one-way)",
            Note = "PBKDF2 hashes, not recoverable secrets. A hash cannot be DPAPI-wrapped (verification " +
                   "would break) and needs no at-rest secrecy beyond being one-way — correctly left alone."
        });

        return rows;
    }

    public async Task<DatabaseSecurityStatus> GetDatabaseStatusAsync()
    {
        var backups = await _backupService.ListBackupsAsync();
        var probe = SafeProbe();

        return new DatabaseSecurityStatus
        {
            ProbeSaysEncrypted = probe,
            AppOpensWithoutKey = true, // App.xaml.cs binds UseSqlite("Data Source=app.db") with no key.
            MasterPasswordState = "Not in use — the app connects to app.db with no key, so no master " +
                                  "password is stored, cached, or asked for at startup.",
            ProbeNote = probe
                ? "The probe could not open app.db un-keyed, which is consistent with an encrypted file. " +
                  "This app never encrypts the file today, so a 'yes' here would be surprising — treat it as " +
                  "a hint, not proof. A wrong-key failure and a genuine ciphertext are indistinguishable to this test."
                : "app.db opened without a key and answered normally, so the file is plaintext. Detection " +
                  "proves the file is readable un-keyed; it cannot prove a file is encrypted merely because a " +
                  "PRAGMA was accepted.",
            BackupExists = backups.Count > 0,
            BackupCount = backups.Count
        };
    }

    public async Task<SecretProtectionReport> ProtectStoredSecretsAsync()
    {
        var report = new SecretProtectionReport();

        // Only the SMTP password has a write path that the protector owns, so it is the only value
        // we re-save here. The API key is deliberately skipped (see GetSecretInventoryAsync) — the
        // transport must decrypt-on-read before its column can hold a ciphertext.
        var messaging = await _dbContext.MessagingSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (messaging == null)
            return report;

        var state = _protector.Classify(messaging.SmtpPassword);
        switch (state)
        {
            case SecretStorageState.Empty:
                break; // nothing stored, nothing to do — not counted as a problem.

            case SecretStorageState.Plaintext:
            case SecretStorageState.LegacyProtected:
                var upgraded = _protector.Protect(messaging.SmtpPassword);
                if (string.Equals(upgraded, messaging.SmtpPassword, StringComparison.Ordinal))
                {
                    // Protect failed open (DPAPI unavailable): be honest, leave it counted as plaintext.
                    report.LeftPlaintext++;
                }
                else
                {
                    messaging.SmtpPassword = upgraded;
                    report.NewlyProtected++;
                    await _dbContext.SaveChangesAsync();
                }
                break;

            case SecretStorageState.Protected:
                report.AlreadyProtected++;
                break;
        }

        // Reflect the values we are intentionally leaving alone so the totals match the inventory.
        var sync = await _dbContext.SyncSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (sync != null && _protector.Classify(sync.ApiKey) == SecretStorageState.Plaintext)
            report.Skipped++;

        return report;
    }

    // --- helpers ---

    private bool SafeProbe()
    {
        try
        {
            return _encryption.IsDatabaseEncrypted();
        }
        catch
        {
            return false; // probing must never surface an exception or a key to the UI.
        }
    }

    private SecretStatusRow Describe(
        string displayName, string location, string? storedValue, bool protectable,
        string protectedNote, string plaintextNote)
    {
        var state = _protector.Classify(storedValue);
        var isProtected = state is SecretStorageState.Protected or SecretStorageState.LegacyProtected;
        return new SecretStatusRow
        {
            DisplayName = displayName,
            Location = location,
            IsSet = state != SecretStorageState.Empty,
            IsProtectedAtRest = isProtected,
            IsPlaintext = state == SecretStorageState.Plaintext,
            ProtectableByBulkAction = protectable,
            StateLabel = state switch
            {
                SecretStorageState.Protected => "Encrypted",
                SecretStorageState.LegacyProtected => "Encrypted (legacy)",
                SecretStorageState.Plaintext => "Plaintext",
                _ => "Empty"
            },
            Note = state switch
            {
                SecretStorageState.Protected or SecretStorageState.LegacyProtected => protectedNote,
                SecretStorageState.Plaintext => plaintextNote,
                _ => "No value stored yet."
            }
        };
    }
}
