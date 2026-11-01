using System.IO;

namespace RetailBillingApp.Services;

public interface IDatabaseService
{
    Task<bool> BackupAsync(string destinationPath);
    Task<bool> RestoreAsync(string backupPath);
    Task<long> GetDatabaseSizeAsync();
}
