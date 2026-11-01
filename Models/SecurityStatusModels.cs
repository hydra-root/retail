namespace RetailBillingApp.Models;

/// <summary>
/// SEC-03: one row in the "Security &amp; Encryption" secret inventory. It describes WHERE a
/// reversible secret lives and how it is currently stored — never the secret itself. Values are
/// deliberately excluded: the screen and any audit payload carry only these flags/labels.
/// </summary>
public class SecretStatusRow
{
    /// <summary>Human name, e.g. "Email (SMTP) password".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Qualified location, e.g. "MessagingSetting.SmtpPassword (Id 1)".</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>True when a value is actually stored (HasKey) — a blank secret is nothing to protect.</summary>
    public bool IsSet { get; set; }

    /// <summary>True when the stored value is DPAPI-protected at rest (canonical or legacy form).</summary>
    public bool IsProtectedAtRest { get; set; }

    /// <summary>True when a stored value is sitting in the clear and could be protected now.</summary>
    public bool IsPlaintext { get; set; }

    /// <summary>True when SEC-03 can protect this value through the current write path. False for
    /// secrets whose read side is not yet routed through the protector (e.g. the HQ API key) —
    /// protecting those before the transport decrypts on read would break the live feature.</summary>
    public bool ProtectableByBulkAction { get; set; }

    /// <summary>Short, value-free status word for the grid: Empty / Encrypted / Encrypted (legacy) / Plaintext.</summary>
    public string StateLabel { get; set; } = "Empty";

    /// <summary>Plain-language note shown beside the row (why it is or is not protected).</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// SEC-03: the outcome of a "protect stored secrets now" run. Counts only — no values, no keys.
/// </summary>
public class SecretProtectionReport
{
    public int NewlyProtected { get; set; }
    public int AlreadyProtected { get; set; }
    public int LeftPlaintext { get; set; }
    public int Skipped { get; set; }

    /// <summary>Value-free summary suitable for a status line or an audit detail.</summary>
    public string Summary =>
        NewlyProtected > 0
            ? $"{NewlyProtected} secret(s) protected, {AlreadyProtected} already secure" +
              (Skipped > 0 ? $", {Skipped} skipped (read side not wired)" : string.Empty) +
              (LeftPlaintext > 0 ? $", {LeftPlaintext} left in the clear" : string.Empty) + "."
            : $"No new secrets to protect — {AlreadyProtected} already secure" +
              (Skipped > 0 ? $", {Skipped} skipped (read side not wired)" : string.Empty) +
              (LeftPlaintext > 0 ? $", {LeftPlaintext} still in the clear" : string.Empty) + ".";
}

/// <summary>
/// SEC-03: an honest snapshot of whole-database encryption state for the screen. Carries a
/// statement of what detection can and cannot prove, because <c>IsDatabaseEncrypted</c> is a
/// heuristic (it probes PRAGMA key on a freshly opened un-keyed connection).
/// </summary>
public class DatabaseSecurityStatus
{
    /// <summary>What the <c>PRAGMA key</c> probe returned. Only meaningful as a hint — see ProbeNote.</summary>
    public bool ProbeSaysEncrypted { get; set; }

    /// <summary>True when the app opened the DB with no key at all (the current, plaintext reality).</summary>
    public bool AppOpensWithoutKey { get; set; } = true;

    /// <summary>Master-password state as the app is configured today: it is never stored or requested.</summary>
    public string MasterPasswordState { get; set; } = "Not in use — the app connects to app.db with no key.";

    /// <summary>Caveat that must be shown to the operator so "encrypted" is not over-claimed.</summary>
    public string ProbeNote { get; set; } = string.Empty;

    /// <summary>Backup presence — the gate the "encrypt database" action refuses to run without.</summary>
    public bool BackupExists { get; set; }

    /// <summary>Count of prior backups found (never their paths-as-secrets; a count is enough).</summary>
    public int BackupCount { get; set; }
}
