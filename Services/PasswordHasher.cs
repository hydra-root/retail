using System.Security.Cryptography;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-04: password hashing for User.PasswordHash. New/updated accounts use
/// PBKDF2 (SHA-256, per-password random salt, time-limited iteration count);
/// legacy SHA-256 hashes keep working and are silently re-hashed on the next
/// successful login/verification. Encoded as:
///   "PBKDF2$<iterations>$<saltHex>$<hashHex>"
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000; // OWASP 2023 recommendation
    private const string Prefix = "PBKDF2";

    /// <summary>Hash a password for storage.</summary>
    public static string Hash(string password)
    {
        var salt = new byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return $"{Prefix}${Iterations}${Convert.ToHexString(salt).ToLowerInvariant()}" +
               $"${Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>Verify a password against a stored hash (any generation).</summary>
    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        // Legacy SHA-256 format: bare hex, verify directly.
        if (!stored.StartsWith(Prefix + "$", StringComparison.OrdinalIgnoreCase))
        {
            var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
            var legacy = Convert.ToHexString(digest).ToLowerInvariant();
            return FixedTimeEqualsAscii(stored, legacy);
        }

        var parts = stored.Split('$');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        if (!TryDecodeHex(parts[2], out var salt) || !TryDecodeHex(parts[3], out var expectedHash)) return false;

        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, expectedHash);
    }

    /// <summary>Legacy hashes are flagged so callers can opportunistically upgrade.</summary>
    public static bool IsLegacy(string stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix + "$", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeHex(string hex, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool FixedTimeEqualsAscii(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
    }
}
