using RetailBillingApp.Data;
using System.IO;

namespace RetailBillingApp.Services;

public class DatabaseService : IDatabaseService
{
    private readonly AppDbContext _dbContext;

    public DatabaseService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> BackupAsync(string destinationPath)
    {
        try
        {
            var sourcePath = "app.db";
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public async Task<bool> RestoreAsync(string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, "app.db", overwrite: true);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public Task<long> GetDatabaseSizeAsync() =>
        Task.FromResult(File.Exists("app.db") ? new FileInfo("app.db").Length : 0L);
}
