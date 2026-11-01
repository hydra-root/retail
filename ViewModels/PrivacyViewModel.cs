using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-10: privacy and compliance. Audit/activity-log retention purging with
/// confirmations, retention preview counts, and GDPR-style CSV data export.
/// </summary>
public partial class PrivacyViewModel : BaseViewModel
{
    private readonly IPrivacyService _privacyService;

    [ObservableProperty]
    private int _retentionDays = 365;

    [ObservableProperty]
    private int _auditPreviewCount;

    [ObservableProperty]
    private int _logsPreviewCount;

    [ObservableProperty]
    private int _exportCustomerId;

    [ObservableProperty]
    private ObservableCollection<PrivacyResultRow> _results = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public PrivacyViewModel(IPrivacyService privacyService)
    {
        _privacyService = privacyService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var (audit, logs) = await _privacyService.GetRetentionPreviewAsync(RetentionDays);
            AuditPreviewCount = audit;
            LogsPreviewCount = logs;
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnRetentionDaysChanged(int value) => _ = LoadDataAsync();

    [RelayCommand]
    private void PreviewRetention() => _ = LoadDataAsync();

    [RelayCommand]
    private async System.Threading.Tasks.Task PurgeAudit()
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Permanently delete {AuditPreviewCount} audit trail record(s) older than {RetentionDays} days?\n\nThis cannot be undone.",
            "Confirm Audit Trail Purge",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        await RunAsync("Purge Audit Trail", async () =>
        {
            var (before, after) = await _privacyService.PurgeAuditTrailAsync(RetentionDaysSafe);
            Results.Insert(0, new PrivacyResultRow
            {
                Action = "Purge Audit Trail",
                Status = "Done",
                Detail = $"Deleted {before - after} record(s) older than {RetentionDays} days ({before} → {after} remaining)."
            });
            SuccessMessage = $"Audit trail purge complete. {before - after} record(s) deleted.";
            await LoadDataAsync();
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PurgeLogs()
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Permanently delete {LogsPreviewCount} activity log record(s) older than {RetentionDays} days?\n\nThis cannot be undone.",
            "Confirm Activity Log Purge",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        await RunAsync("Purge Activity Logs", async () =>
        {
            var (before, after) = await _privacyService.PurgeActivityLogsAsync(RetentionDaysSafe);
            Results.Insert(0, new PrivacyResultRow
            {
                Action = "Purge Activity Logs",
                Status = "Done",
                Detail = $"Deleted {before - after} record(s) older than {RetentionDays} days ({before} → {after} remaining)."
            });
            SuccessMessage = $"Activity log purge complete. {before - after} record(s) deleted.";
            await LoadDataAsync();
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Export()
    {
        if (ExportCustomerId <= 0)
        {
            ErrorMessage = "Enter a valid customer ID to export.";
            return;
        }

        await RunAsync("Export Customer Data", async () =>
        {
            var path = await _privacyService.ExportCustomerDataAsync(ExportCustomerId, "CSV");
            Results.Insert(0, new PrivacyResultRow
            {
                Action = "Export Customer Data",
                Status = "Done",
                Detail = $"Exported customer {ExportCustomerId} to {path}"
            });
            SuccessMessage = $"Data exported to {path}";
        });
    }

    /// <summary>Retention window clamped to a sane minimum so a purge never sweeps everything.</summary>
    private int RetentionDaysSafe => Math.Max(RetentionDays, 1);

    private async System.Threading.Tasks.Task RunAsync(string action, Func<System.Threading.Tasks.Task> work)
    {
        IsLoading = true;
        try
        {
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;
            await work();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{action} failed: {ex.Message}";
            Results.Insert(0, new PrivacyResultRow
            {
                Action = action,
                Status = "Failed",
                Detail = ex.Message
            });
        }
        finally
        {
            IsLoading = false;
        }
    }
}
