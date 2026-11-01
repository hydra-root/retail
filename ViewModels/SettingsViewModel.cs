using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Windows;

namespace RetailBillingApp.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IBackupService _backupService;
    private readonly IDatabaseService _databaseService;

    [ObservableProperty]
    private string _appName = "Retail Billing & Inventory";

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _companyAddress = string.Empty;

    [ObservableProperty]
    private string _companyGstNumber = string.Empty;

    [ObservableProperty]
    private string _companyPhone = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _databaseHealth = "Unknown";

    public SettingsViewModel(IBackupService backupService, IDatabaseService databaseService)
    {
        _backupService = backupService;
        _databaseService = databaseService;
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        IsBusy = true;
        var result = await _backupService.BackupDatabaseAsync();
        IsBusy = false;

        StatusMessage = result.Success
            ? $"{result.Message} Saved to: {result.Path}"
            : result.Message;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        IsBusy = true;
        var success = await _backupService.RestoreDatabaseAsync();
        IsBusy = false;

        StatusMessage = success
            ? "Restore completed. Please restart the application."
            : "Restore cancelled or failed.";
    }

    [RelayCommand]
    private async Task VerifyDatabaseAsync()
    {
        IsBusy = true;
        var check = await _backupService.RunIntegrityCheckAsync();
        IsBusy = false;

        DatabaseHealth = check;
        StatusMessage = string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase)
            ? "Database integrity check passed (quick_check: ok)."
            : $"Database integrity check result: {check}";
    }

    [RelayCommand]
    private async Task GetDbSizeAsync()
    {
        var size = await _databaseService.GetDatabaseSizeAsync();
        StatusMessage = $"Database size: {size / 1024.0:F2} KB";
    }

    [RelayCommand]
    private async Task ListBackupsAsync()
    {
        var backups = await _backupService.ListBackupsAsync();
        StatusMessage = backups.Count == 0
            ? "No backups found."
            : $"{backups.Count} backup(s). Latest: {backups[0].FileName} ({backups[0].SizeBytes / 1024.0:F1} KB, {backups[0].CreatedAt:dd-MMM-yyyy HH:mm})";
    }
}

