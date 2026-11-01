using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using RetailBillingApp.Data;
using System.IO;
using System.Windows;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-02: Safe manual backup/restore.
/// Backups run after a WAL checkpoint and are verified with
/// PRAGMA quick_check before being used; restore refuses a corrupt backup file.
/// </summary>
public class BackupService : IBackupService
{
    private readonly IDatabaseService _databaseService;

    /// <summary>SEC-03: the live database is SQLCipher-encrypted; migrations are never triggered from here.</summary>
    private static string ConnectionString => EncryptionService.GetAppConnectionString(migrateIfPlaintext: false);

    public BackupService(IDatabaseService databaseService) => _databaseService = databaseService;

    public string GetDefaultBackupPath()
    {
        var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetailBillingBackups");
        Directory.CreateDirectory(backupDir);
        return Path.Combine(backupDir, $"backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.db");
    }

    public async Task<BackupResult> BackupDatabaseAsync(string? destinationPath = null)
    {
        try
        {
            if (!File.Exists("app.db"))
                return new BackupResult { Success = false, Message = "Database file not found." };

            // Flush the WAL so the copied file is complete
            await RunOnDbAsync(ConnectionString,
                "PRAGMA wal_checkpoint(TRUNCATE);");

            var integrity = await RunScalarOnDbAsync(ConnectionString, "PRAGMA quick_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                return new BackupResult { Success = false, Message = $"Source database is corrupt: {integrity}. Backup aborted." };

            var path = destinationPath ?? GetDefaultBackupPath();
            File.Copy("app.db", path, overwrite: true);

            // Verify the backup copy itself (works for encrypted and legacy plaintext copies)
            var backupCheck = await QuickCheckBackupAsync(path);
            if (!string.Equals(backupCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                return new BackupResult { Success = false, Message = "Backup verification failed; the copy was deleted. Please try again." };
            }

            var size = new FileInfo(path).Length;
            return new BackupResult
            {
                Success = true,
                Path = path,
                SizeBytes = size,
                Message = $"Backup OK ({size / 1024.0:F1} KB)"
            };
        }
        catch (Exception ex)
        {
            return new BackupResult { Success = false, Message = $"Backup failed: {ex.Message}" };
        }
    }

    public async Task<bool> RestoreDatabaseAsync(string? backupPath = null)
    {
        if (string.IsNullOrEmpty(backupPath))
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Database files (*.db)|*.db|All files (*.*)|*.*",
                Title = "Select Backup File to Restore"
            };

            if (dialog.ShowDialog() != true) return false;
            backupPath = dialog.FileName;
        }

        // Verify the backup file before touching the live database (encrypted or legacy plaintext)
        var check = "not ok";
        try
        {
            check = await QuickCheckBackupAsync(backupPath);
        }
        catch
        {
            // unreadable file
        }

        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"The selected file is not a valid, undamaged database ({check}).\nRestore cancelled.",
                "Restore Aborted", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var result = MessageBox.Show(
            "This will replace the current database with the backup.\n" +
            "The application will need to be restarted afterwards. Continue?",
            "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return false;

        var ok = await _databaseService.RestoreAsync(backupPath);
        if (ok)
        {
            // Open the restored file once so any WAL journal is resolved cleanly
            await RunOnDbAsync(ConnectionString, "PRAGMA wal_checkpoint(TRUNCATE);");

            MessageBox.Show(
                "Restore completed successfully.\nPlease restart the application to load the restored data.",
                "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return ok;
    }

    public Task<List<BackupInfo>> ListBackupsAsync()
    {
        try
        {
            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RetailBillingBackups");
            if (!Directory.Exists(backupDir))
                return Task.FromResult(new List<BackupInfo>());

            var list = Directory.GetFiles(backupDir, "*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.CreationTime)
                .Select(fi => new BackupInfo
                {
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    CreatedAt = fi.CreationTime
                })
                .ToList();

            return Task.FromResult(list);
        }
        catch
        {
            return Task.FromResult(new List<BackupInfo>());
        }
    }

    public async Task<string> RunIntegrityCheckAsync()
    {
        try
        {
            return await RunScalarOnDbAsync(ConnectionString, "PRAGMA quick_check;");
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    // ---- helpers ----

    /// <summary>
    /// SEC-03: quick_check a backup file regardless of its format. New backups are encrypted
    /// copies and need the key; legacy plaintext backups open without one.
    /// </summary>
    private static async Task<string> QuickCheckBackupAsync(string path)
    {
        var key = EncryptionService.TryGetKey();
        if (key != null)
        {
            try
            {
                return await RunScalarOnDbAsync($"Data Source={path};Password={key}", "PRAGMA quick_check;");
            }
            catch (SqliteException)
            {
                // Not an encrypted file (or wrong key) — try the legacy plaintext path below.
            }
        }

        return await RunScalarOnDbAsync($"Data Source={path}", "PRAGMA quick_check;");
    }

    private static async Task RunOnDbAsync(string cs, string sql)
    {
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> RunScalarOnDbAsync(string cs, string sql)
    {
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var scalar = await cmd.ExecuteScalarAsync();
        return scalar?.ToString() ?? "";
    }
}
