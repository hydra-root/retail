using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;

namespace RetailBillingApp.Services;

public interface IEncryptionService
{
    string GetConnectionString(string masterPassword);
    string EncryptString(string plainText);
    string DecryptString(string cipherText);
    bool IsProtected(string value);
    byte[] DeriveKey(string password, byte[] salt);
    bool IsDatabaseEncrypted();
}

/// <summary>
/// SEC-03: makes at-rest encryption real. The SQLitePCLRaw e_sqlcipher build is wired in the
/// csproj (Microsoft.Data.Sqlite.Core + SQLitePCLRaw.bundle_e_sqlcipher 2.1.11 — earlier 2.1.6
/// shipped a native library incompatible with the core that Microsoft.Data.Sqlite.Core 10 pulls,
/// which surfaced as a TypeLoadException on init). The key is 32 random bytes, stored
/// DPAPI-protected (CurrentUser + app entropy) in app.db.key next to the database; it never
/// appears in logs or audit lines.
/// A legacy plaintext app.db is converted in place once at startup via ATTACH + sqlcipher_export,
/// with a app.db.pre-cipher.bak rollback copy kept beside it.
/// </summary>
public class EncryptionService : IEncryptionService
{
    static EncryptionService() => SQLitePCL.Batteries_V2.Init();

    private static readonly byte[] _entropy = System.Text.Encoding.UTF8.GetBytes("RetailBillingApp_2026");

    /// <summary>Mark on values returned by <see cref="EncryptString"/> so plain and protected strings can be told apart.</summary>
    private const string ProtectedPrefix = "dpapi1:";

    // ---- SEC-03: database key + migration (static, called from App startup / DI wiring) ----

    /// <summary>Hex key of the SQLCipher database, or null when not initialized yet.</summary>
    private static string? _appKey;

    /// <summary>Relative on purpose: keeps the historical per-working-directory app.db behaviour.</summary>
    private const string DbFile = "app.db";
    private const string KeyFile = "app.db.key";
    private const string CipherTemp = "app.cipher.tmp";
    private const string RollbackCopy = "app.db.pre-cipher.bak";

    /// <summary>
    /// Startup entry point: initializes the e_sqlcipher provider, unseals (or creates) the DPAPI
    /// key file, converts a legacy plaintext app.db to SQLCipher once, and returns the keyed
    /// connection string for the DbContext. Call with migrateIfPlaintext:false from runtime
    /// services (backups) — they must never move the database file underneath a live EF context.
    /// </summary>
    public static string GetAppConnectionString(bool migrateIfPlaintext = true)
    {
        var key = GetOrCreateKey();
        if (migrateIfPlaintext && File.Exists(DbFile) && IsPlaintextFile(DbFile))
            MigrateToEncrypted(key);
        return $"Data Source={DbFile};Password={key}";
    }

    /// <summary>The key for direct connections (backup validation), null before first initialization.</summary>
    public static string? TryGetKey()
    {
        try { _appKey ??= ReadKeyFile(); } catch { _appKey = null; }
        return _appKey;
    }

    /// <summary>True when the file is a plaintext SQLite db ("SQLite format 3" magic header).</summary>
    public static bool IsPlaintextFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 16) return false;
            Span<byte> header = stackalloc byte[16];
            stream.ReadExactly(header);
            return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
        }
        catch
        {
            return false;
        }
    }

    private static string GetOrCreateKey()
    {
        if (_appKey != null) return _appKey;

        if (File.Exists(KeyFile))
        {
            try
            {
                _appKey = ReadKeyFile();
                return _appKey;
            }
            catch (Exception ex)
            {
                // The existing database cannot be opened without its key — fail loud, never
                // roll a new key that would brick an encrypted file.
                if (File.Exists(DbFile) && !IsPlaintextFile(DbFile))
                    throw new InvalidOperationException(
                        "The database key file exists but could not be unsealed on this Windows " +
                        $"user profile ({ex.Message}). Restore the key file or a full backup on the same profile.", ex);
                // No (plaintext) database to lose: a broken key file is disposable.
                File.Delete(KeyFile);
            }
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        _appKey = Convert.ToHexString(bytes);
        File.WriteAllText(KeyFile, Protect(_appKey));
        return _appKey;
    }

    private static string ReadKeyFile() => Unprotect(File.ReadAllText(KeyFile));

    private static void MigrateToEncrypted(string key)
    {
        // Rollback safety net before touching anything.
        File.Copy(DbFile, RollbackCopy, overwrite: true);
        File.Delete(CipherTemp);

        // Make sure the plaintext WAL is empty, so the file copy is complete.
        using (var conn = new SqliteConnection($"Data Source={DbFile};Pooling=false"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        using (var conn = new SqliteConnection($"Data Source={DbFile};Pooling=false"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"ATTACH DATABASE '{CipherTemp}' AS cipher KEY '{key}';" +
                                  "SELECT sqlcipher_export('cipher');" +
                                  "DETACH DATABASE cipher;";
                cmd.ExecuteNonQuery();
            }
        }

        // Verify the converted file really is a cipher file before trusting it.
        if (!File.Exists(CipherTemp) || IsPlaintextFile(CipherTemp) || new FileInfo(CipherTemp).Length == 0)
            throw new InvalidOperationException("SQLCipher export did not produce an encrypted database; app.db was left untouched.");

        // Swap files; WAL/SHM belong to the old plaintext file and are dropped.
        foreach (var side in new[] { DbFile + "-wal", DbFile + "-shm" })
            if (File.Exists(side)) File.Delete(side);
        File.Delete(DbFile);
        File.Move(CipherTemp, DbFile);
    }

    // ---- SEC-03: stored-secret protection (per-setting secrets, e.g. the SMTP password) ----

    public string GetConnectionString(string masterPassword)
    {
        if (string.IsNullOrWhiteSpace(masterPassword))
            return "Data Source=" + DbFile;

        var key = DeriveKey(masterPassword, _entropy);
        var hexKey = Convert.ToHexString(key);
        return $"Data Source={DbFile};Password={hexKey};";
    }

    public byte[] DeriveKey(string password, byte[] salt)
    {
        using var derive = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        return derive.GetBytes(32);
    }

    public string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText) || IsProtected(plainText))
            return plainText;

        try
        {
            return ProtectedPrefix + Protect(plainText);
        }
        catch
        {
            return plainText;
        }
    }

    /// <summary>True when the value carries the protected-prefix and must go through DecryptString.</summary>
    public bool IsProtected(string value) =>
        value != null && value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    public string DecryptString(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText) || !IsProtected(cipherText))
            return cipherText;

        try
        {
            return Unprotect(cipherText[ProtectedPrefix.Length..]);
        }
        catch
        {
            return cipherText;
        }
    }

    // ---- DPAPI wrappers (key file + per-setting secrets share the same entropy) ----

    private static string Protect(string plainText)
    {
        var encrypted = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plainText),
            _entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string base64Text)
    {
        var decrypted = ProtectedData.Unprotect(
            Convert.FromBase64String(base64Text),
            _entropy,
            DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(decrypted);
    }

    // ---- SEC-03: honest database-state report (header test, not a pragma guess) ----

    public bool IsDatabaseEncrypted()
    {
        if (!File.Exists(DbFile)) return true; // nothing on disk yet; will be created encrypted
        return !IsPlaintextFile(DbFile);
    }
}
