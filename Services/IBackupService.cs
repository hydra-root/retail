using System.IO;

namespace RetailBillingApp.Services;

public interface IBackupService
{
    Task<BackupResult> BackupDatabaseAsync(string? destinationPath = null);
    Task<bool> RestoreDatabaseAsync(string? backupPath = null);
    string GetDefaultBackupPath();
    Task<List<BackupInfo>> ListBackupsAsync();
    Task<string> RunIntegrityCheckAsync();
}

public class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BackupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }
    public long SizeBytes { get; set; }
}
