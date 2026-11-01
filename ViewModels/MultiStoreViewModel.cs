using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>USR-02: consolidated multi-store dashboard with per-store drill-down.</summary>
public partial class MultiStoreViewModel : BaseViewModel
{
    private readonly IStoreService _storeService;
    private readonly IActivityLogService _activityLogService;
    private readonly IAuthService _authService;

    [ObservableProperty]
    private ObservableCollection<Store> _stores = new();

    [ObservableProperty]
    private Store? _selectedStore;

    [ObservableProperty]
    private ObservableCollection<StoreSalesSummary> _storeSales = new();

    [ObservableProperty]
    private ObservableCollection<ActivityLog> _recentActivity = new();

    [ObservableProperty]
    private decimal _totalSales;

    [ObservableProperty]
    private int _totalInvoices;

    [ObservableProperty]
    private int _storeCount;

    [ObservableProperty]
    private int _activeUserCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public MultiStoreViewModel(IStoreService storeService, IActivityLogService activityLogService, IAuthService authService)
    {
        _storeService = storeService;
        _activityLogService = activityLogService;
        _authService = authService;
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            Stores = new ObservableCollection<Store>(await _storeService.GetAllStoresAsync());
            await LoadSalesAsync();

            if (SelectedStore != null)
                await LoadActivitiesAsync();

            StoreCount = Stores.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to load multi-store data: " + ex.Message;
        }

        IsLoading = false;
    }

    private async Task LoadSalesAsync()
    {
        var summaries = await _storeService.GetStoreSalesSummaryAsync();

        if (SelectedStore != null)
        {
            summaries = summaries.Where(s => s.StoreId == SelectedStore.Id).ToList();
        }

        StoreSales = new ObservableCollection<StoreSalesSummary>(summaries);
        TotalSales = summaries.Sum(s => s.TotalSales);
        TotalInvoices = summaries.Sum(s => s.InvoiceCount);
    }

    private async Task LoadActivitiesAsync()
    {
        var logs = await _activityLogService.SearchLogsAsync(count: 50);
        if (SelectedStore != null)
        {
            logs = logs.Where(l => l.StoreId == SelectedStore.Id).ToList();
        }
        RecentActivity = new ObservableCollection<ActivityLog>(logs.Take(20));
    }

    partial void OnSelectedStoreChanged(Store? value)
    {
        if (value != null)
            _ = OnStoreFilterChangedAsync();
    }

    [RelayCommand]
    private async Task OnStoreFilterChangedAsync()
    {
        if (SelectedStore == null)
        {
            await ClearFilterAsync();
            return;
        }
        try
        {
            var summaries = await _storeService.GetStoreSalesSummaryAsync();
            summaries = summaries.Where(s => s.StoreId == SelectedStore.Id).ToList();
            StoreSales = new ObservableCollection<StoreSalesSummary>(summaries);
            TotalSales = summaries.Sum(s => s.TotalSales);
            TotalInvoices = summaries.Sum(s => s.InvoiceCount);

            var logs = await _activityLogService.SearchLogsAsync(count: 50);
            logs = logs.Where(l => l.StoreId == SelectedStore.Id).ToList();
            RecentActivity = new ObservableCollection<ActivityLog>(logs.Take(20));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to filter by store: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        SelectedStore = null;
        var summaries = await _storeService.GetStoreSalesSummaryAsync();
        StoreSales = new ObservableCollection<StoreSalesSummary>(summaries);
        TotalSales = summaries.Sum(s => s.TotalSales);
        TotalInvoices = summaries.Sum(s => s.InvoiceCount);

        var logs = await _activityLogService.SearchLogsAsync(count: 50);
        RecentActivity = new ObservableCollection<ActivityLog>(logs.Take(20));

        StoreCount = Stores.Count;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();
}
