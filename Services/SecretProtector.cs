using System.Security.Cryptography;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-03 (encrypted data at rest) — the single, honest seam for protecting reversible
/// secrets (SMTP password, HQ API key, …) before they are written into SQLite columns.
///
/// Design: value stays PLAINTEXT in memory for callers, and is stored DPAPI-protected with a
/// deterministic <c>ENC:</c> discriminator. The prefix is what makes this safe where a bare
/// base64 blob is not — raw <see cref="ProtectedData"/> output is also base64, so "is this
/// encrypted?" cannot be answered by shape alone; a plaintext password that happens to be
/// valid base64 would be misread as a blob. The prefix settles it without a guess.
///
/// Backward/forward compatibility (this is deliberate, because the DB is LIVE and must never
/// be corrupted):
///   • legacy clear-text rows   → <see cref="Unprotect"/> returns them unchanged;
///   • bare base64 DPAPI blobs  → written by the pre-existing
///                                <see cref="EncryptionService.EncryptString"/> (no prefix).
///                                <see cref="Unprotect"/> still decodes them, and
///                                <see cref="Protect"/> upgrades them to the <c>ENC:</c> form;
///   • <c>ENC:</c> blobs        → the canonical form this class writes.
/// <see cref="Protect"/> is idempotent: protecting an already-protected value returns the same
/// value, so a double-save never stacks a second encryption layer.
///
/// Same entropy string and the same <see cref="DataProtectionScope.CurrentUser"/> as
/// <see cref="EncryptionService"/>, so a value protected by either is readable by the other.
/// On any failure the input is returned untouched — losing a secret is worse than leaving it
/// in its previous state, and no exception may leak the value into a log.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Protect a secret for storage. Empty/whitespace and already-protected values pass
    /// through unchanged; a legacy blob or plaintext is upgraded to the <c>ENC:</c> canonical form.</summary>
    string Protect(string? plainText);

    /// <summary>Return the plaintext a caller needs. Understands <c>ENC:</c>, legacy bare DPAPI base64,
    /// and plain clear-text rows; anything it cannot decrypt is returned unchanged (never corrupted).</summary>
    string Unprotect(string? storedValue);

    /// <summary>Classify one stored value without ever handing back its plaintext — for reporting only.</summary>
    SecretStorageState Classify(string? storedValue);
}

/// <summary>How a stored secret is currently sitting in the database. Never carries the value.</summary>
public enum SecretStorageState
{
    /// <summary>No value stored.</summary>
    Empty,

    /// <summary>Readable in the clear — the state SEC-03 is meant to eliminate.</summary>
    Plaintext,

    /// <summary>DPAPI-protected in the canonical <c>ENC:</c> form written by <see cref="ISecretProtector"/>.</summary>
    Protected,

    /// <summary>DPAPI-protected but in the older bare-base64 form written by <see cref="EncryptionService"/>.
    /// Genuinely encrypted at rest, just not yet upgraded to the discriminator form.</summary>
    LegacyProtected
}

public class SecretProtector : ISecretProtector
{
    /// <summary>The clean discriminator: an <c>ENC:</c> prefix on the stored string.</summary>
    public const string Prefix = "ENC:";

    // MUST stay byte-for-byte equal to EncryptionService._entropy so blobs written by the
    // older service decrypt here and vice-versa (the live app.db may already hold such blobs).
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RetailBillingApp_2026");

    public string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText ?? string.Empty;

        // Already in canonical form — do not add a second DPAPI layer.
        if (plainText.StartsWith(Prefix, StringComparison.Ordinal))
            return plainText;

        // A legacy bare-DPAPI blob, or genuine plaintext: normalise to plaintext first, then protect.
        var secret = UnprotectToPlainText(plainText);
        if (string.IsNullOrEmpty(secret))
            return plainText; // nothing meaningful to protect; leave the original alone.

        try
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            // DPAPI unavailable (odd profile / non-interactive user). Fail open by returning the
            // value as it was; the caller still works and Security & Encryption reports it plaintext.
            return plainText;
        }
    }

    public string Unprotect(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
            return storedValue ?? string.Empty;

        var plaintext = UnprotectToPlainText(storedValue);
        // UnprotectToPlainText returns the input unchanged when it is plaintext or undecryptable,
        // so callers (SMTP client, in-memory form) always get something usable.
        return plaintext;
    }

    public SecretStorageState Classify(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
            return SecretStorageState.Empty;

        if (storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return CanDecrypt(storedValue.Substring(Prefix.Length))
                ? SecretStorageState.Protected
                : SecretStorageState.Plaintext; // looks like ours but DPAPI rejected it — treat as text

        // No prefix: either a legacy bare-base64 DPAPI blob, or a genuine clear-text value.
        return IsLegacyBlob(storedValue) ? SecretStorageState.LegacyProtected : SecretStorageState.Plaintext;
    }

    // --- internals (never throw, never leak the secret) ---

    private static string UnprotectToPlainText(string stored)
    {
        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var body = stored.Substring(Prefix.Length);
            if (TryUnprotectBase64(body, out var text))
                return text;
            return stored; // cannot decrypt — return as-is so we never corrupt it
        }

        if (TryUnprotectBase64(stored, out var legacyText))
            return legacyText; // legacy bare DPAPI blob upgraded on the next Protect()

        return stored; // clear-text row — return verbatim
    }

    private static bool CanDecrypt(string base64) => TryUnprotectBase64(base64, out _);

    private static bool IsLegacyBlob(string value)
    {
        // A base64 string that DPAPI successfully unprotects is a legacy blob; anything else
        // (including valid base64 that is not a DPAPI payload) is treated as plaintext. The cheap
        // length guard avoids decoding obvious plaintext before the real test in TryUnprotectBase64.
        return value.Length > 24 && TryUnprotectBase64(value, out _);
    }

    private static bool TryUnprotectBase64(string base64, out string plaintext)
    {
        plaintext = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(base64); // throws for non-base64 → treated as plaintext
            var opened = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(opened);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
