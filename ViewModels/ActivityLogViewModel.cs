using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>USR-07: activity log viewer with filters.</summary>
public partial class ActivityLogViewModel : BaseViewModel
{
    private readonly IActivityLogService _activityLogService;

    [ObservableProperty]
    private ObservableCollection<ActivityLog> _logs = new();

    [ObservableProperty]
    private string _usernameFilter = string.Empty;

    [ObservableProperty]
    private string _actionFilter = string.Empty;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ActivityLogViewModel(IActivityLogService activityLogService)
    {
        _activityLogService = activityLogService;
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var logs = await _activityLogService.SearchLogsAsync(
                string.IsNullOrWhiteSpace(UsernameFilter) ? null : UsernameFilter,
                string.IsNullOrWhiteSpace(ActionFilter) ? null : ActionFilter,
                FromDate,
                ToDate,
                count: 1000);

            Logs = new ObservableCollection<ActivityLog>(logs);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to load activity log: " + ex.Message;
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();
}
