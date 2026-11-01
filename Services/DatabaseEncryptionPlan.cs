using System.Security.Cryptography;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-03 item 3 — the whole-database-encryption design, shipped as a DOCUMENT and a set of
/// pure string/decision helpers, deliberately NOT wired into <c>AppDbContext.OnConfiguring</c>.
///
/// Nothing here opens, reads, re-keys or migrates <c>app.db</c>. That is intentional: the live
/// database is the user's, and a keyed-connection migration that goes wrong is unrecoverable.
/// The coordinator should treat this as the reference for an explicit, backup-first, single-user
/// action — not something the app does silently at startup.
/// </summary>
public static class DatabaseEncryptionPlan
{
    /// <summary>The file the app opens (relative to the working directory), matching App.xaml.cs.</summary>
    public const string DatabaseFile = "app.db";

    /// <summary>PBKDF2 parameters that match EncryptionService.DeriveKey (SHA-256, 10 000 iterations),
    /// so the raw key agrees with the value <c>GetConnectionString</c> already produces.</summary>
    public static byte[] DeriveRawKey(string masterPassword)
    {
        var salt = Encoding.UTF8.GetBytes("RetailBillingApp_2026"); // same entropy as EncryptionService
        using var derive = new Rfc2898DeriveBytes(masterPassword, salt, 10000, HashAlgorithmName.SHA256);
        return derive.GetBytes(32);
    }

    /// <summary>
    /// The exact statement SQLCipher needs to open an encrypted file. It is applied on an ALREADY
    /// OPENED connection before any other command — <c>Password=</c> in the connection string is NOT
    /// honoured by Microsoft.Data.Sqlite/EF, which is the single most important fact about this path.
    /// </summary>
    public static string BuildPragmaKeySql(string masterPassword)
    {
        var hex = Convert.ToHexString(DeriveRawKey(masterPassword));
        return $"PRAGMA key = \"x'{hex}'\";";
    }

    /// <summary>
    /// The correct way to hand EF a keyed connection: open the SqliteConnection yourself, run
    /// PRAGMA key, THEN give that live connection to UseSqlite — not a connection string. Paste
    /// into App.xaml.cs in place of <c>options.UseSqlite("Data Source=app.db")</c> ONLY when the
    /// whole migration below has been done and the user has accepted the risks.
    /// </summary>
    public static string BuildKeyedConnectionGuidance(string masterPassword) => $$"""
        // NOT WIRED. Requires the user's master password at every startup (see "Remembering it").
        //
        //   var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=app.db");
        //   conn.Open();
        //   using (var pragma = conn.CreateCommand())
        //   {
        //       pragma.CommandText = "{{BuildPragmaKeySql(masterPassword).Replace("\"", "\"\"")}}";
        //       pragma.ExecuteNonQuery();                 // must be the FIRST statement on the handle
        //   }
        //   optionsBuilder.UseSqlite(conn);               // EF reuses the already-keyed handle
        //
        // EncryptionService.GetConnectionString() returns "Data Source=app.db;Password=<hex>;" —
        // that Password keyword is a no-op for Microsoft.Data.Sqlite and does NOT encrypt or unlock;
        // it must not be used as-is with EF. Only the PRAGMA key above actually keys the handle.
        """;

    /// <summary>What happens to a pre-existing plaintext file: it cannot simply be opened encrypted.
    /// This is the full, honest procedure the user must run with a prior backup.</summary>
    public static string DescribeMigrationPlan() => """
        Encrypting an existing plaintext app.db is a data-destructive-if-wrong operation, so it is
        opt-in, single-user, and backup-first. There is no in-place "switch encryption on" for SQLite.

        1. BACKUP FIRST (mandatory). Close every writer, copy app.db (and app.db-wal/-shm) to a dated
           backup OUTSIDE the app folder, and confirm it restores. This screen will not offer the
           action until at least one prior backup is detected.

        2. EXPORT-AND-RE-KEY (the safe direction), not an in-place re-key:
           a. Open the PLAINTEXT file un-keyed.
           b. Open a NEW empty file and immediately issue PRAGMA key on it.
           c. Attach the plaintext file and copy every table across:
                  ATTACH DATABASE 'app.db' AS plain KEY '';
                  (backup each table) INSERT INTO main.<t> SELECT * FROM plain.<t>;
              SQLCipher exposes sqlcipher_export for this in the full build; the raw bundle used here
              is the Community edition and may require the table-by-table copy instead.
           d. DETACH, close, and verify the new file refuses an un-keyed open and accepts the key.

        3. SWAP CAREFULLY: rename only after step 2d passes, keeping the plaintext backup until a
           full session runs against the encrypted file. Never delete app.db as part of this.

        FAILURE MODES the plan must account for:
          • Wrong key → the file reports "file is not a database" / SQLiteError.NotADatabase. There is
            no "close enough" with SQLCipher: a wrong key looks identical to a corrupt file. Keep the
            backup.
          • Missing native bundle → if the e_sqlcipher native library is not deployed, PRAGMA key is a
            no-op and the app silently keeps writing PLAINTEXT while believing it is encrypted. Verify
            the bundle (SQLitePCLRaw.bundle_e_sqlcipher is referenced) and that a keyed open actually
            rejects an un-keyed read before trusting any "encrypted" claim.
          • EF EnsureCreated + DatabaseSchemaUpgrader interplay → both open the connection to run DDL.
            If the connection is not keyed BEFORE the first EF command, EnsureCreated will fail on a
            real encrypted file (or create a plaintext one by accident). The keyed handle must be the
            one handed to DbContextOptions, and DatabaseSchemaUpgrader.Apply must reuse that same
            already-keyed context, not open a second un-keyed connection.
          • WAL/journals → -wal and -shm files are also sensitive; include them in the backup and the
            swap or you leak recent transactions in plaintext next to the encrypted main file.

        REMEMBERING THE MASTER PASSWORD (it must NOT be stored in the DB — that is circular):
          • Acceptable A: DPAPI-protected local file (SecretProtector-style, CurrentUser scope) holding
            the derived key, so the app unlocks without re-asking on the same Windows account. Ties the
            vault to that user profile; a new machine/profile needs the password again.
          • Acceptable B: prompt-at-startup via a PasswordBox read in code-behind (never bound, never
            logged, never refilled), holding the key only in memory for the session.
          • NOT acceptable: storing the password or the derived key in app.db, in config, or anywhere
            the encrypted file itself can decrypt it.

        VERDICT: shipping the automatic path is unsafe against the user's live file. This design is
        provided so the coordinator can gate a manual, backup-first action behind an explicit operator
        decision — the app should keep opening app.db un-keyed (today's behaviour) until that decision
        is made deliberately.
        """;

    /// <summary>Gate for enabling the "encrypt database" control: refuses unless a backup exists, the
    /// session is single-user, and the operator has acknowledged the manual, no-undo nature.</summary>
    public static bool CanAttemptEncryption(bool backupExists, bool singleUserConfirmed, bool riskAcknowledged)
        => backupExists && singleUserConfirmed && riskAcknowledged;
}
