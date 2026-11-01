using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>USR-01: store management (create/edit stores, GSTIN, series, printer).</summary>
public partial class StoreViewModel : BaseViewModel
{
    private readonly IStoreService _storeService;
    private readonly IAuthService _authService;
    private readonly IActivityLogService _activityLogService;

    [ObservableProperty]
    private ObservableCollection<Store> _stores = new();

    [ObservableProperty]
    private Store? _selectedStore;

    [ObservableProperty]
    private string _newStoreName = string.Empty;

    [ObservableProperty]
    private string _newStoreLocation = string.Empty;

    [ObservableProperty]
    private string _newStoreContact = string.Empty;

    [ObservableProperty]
    private string _newStoreGstin = string.Empty;

    [ObservableProperty]
    private string _newStoreAddress = string.Empty;

    [ObservableProperty]
    private string _newStorePhone = string.Empty;

    [ObservableProperty]
    private string _newStoreEmail = string.Empty;

    [ObservableProperty]
    private string _newStoreInvoicePrefix = string.Empty;

    [ObservableProperty]
    private string _newStorePrinterName = string.Empty;

    [ObservableProperty]
    private bool _newStoreIsActive = true;

    [ObservableProperty]
    private ObservableCollection<StoreStockSummary> _storeStock = new();

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public StoreViewModel(IStoreService storeService, IAuthService authService, IActivityLogService activityLogService)
    {
        _storeService = storeService;
        _authService = authService;
        _activityLogService = activityLogService;
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        var stores = await _storeService.GetAllStoresAsync();
        Stores = new ObservableCollection<Store>(stores);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    partial void OnSelectedStoreChanged(Store? value)
    {
        if (value != null)
            _ = LoadStoreStockAsync(value.Id);
        else
            StoreStock.Clear();
    }

    private async Task LoadStoreStockAsync(int storeId)
    {
        var stock = await _storeService.GetStoreStockAsync(storeId);
        StoreStock = new ObservableCollection<StoreStockSummary>(stock);
    }

    [RelayCommand]
    private async Task AddStoreAsync()
    {
        if (string.IsNullOrWhiteSpace(NewStoreName))
        {
            ErrorMessage = "Store name is required.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(NewStoreGstin) && NewStoreGstin.Trim().Length != 15)
        {
            ErrorMessage = "GSTIN must be exactly 15 characters.";
            return;
        }

        // Only one default/primary store: the first created store must be active
        var store = new Store
        {
            Name = NewStoreName.Trim(),
            Location = string.IsNullOrWhiteSpace(NewStoreLocation) ? null : NewStoreLocation.Trim(),
            Contact = string.IsNullOrWhiteSpace(NewStoreContact) ? null : NewStoreContact.Trim(),
            GSTIN = string.IsNullOrWhiteSpace(NewStoreGstin) ? null : NewStoreGstin.Trim().ToUpperInvariant(),
            Address = string.IsNullOrWhiteSpace(NewStoreAddress) ? null : NewStoreAddress.Trim(),
            Phone = string.IsNullOrWhiteSpace(NewStorePhone) ? null : NewStorePhone.Trim(),
            Email = string.IsNullOrWhiteSpace(NewStoreEmail) ? null : NewStoreEmail.Trim(),
            InvoicePrefix = string.IsNullOrWhiteSpace(NewStoreInvoicePrefix) ? null : NewStoreInvoicePrefix.Trim(),
            PrinterName = string.IsNullOrWhiteSpace(NewStorePrinterName) ? null : NewStorePrinterName.Trim(),
            IsActive = NewStoreIsActive
        };

        var success = await _storeService.AddStoreAsync(store);
        if (success)
        {
            // keep main fields for next entry, but clear
            NewStoreName = string.Empty;
            NewStoreLocation = string.Empty;
            NewStoreContact = string.Empty;
            NewStoreGstin = string.Empty;
            NewStoreAddress = string.Empty;
            NewStorePhone = string.Empty;
            NewStoreEmail = string.Empty;
            NewStoreInvoicePrefix = string.Empty;
            NewStorePrinterName = string.Empty;
            ErrorMessage = string.Empty;

            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                store.Id, "StoreCreated", "Store", store.Name, $"Created store '{store.Name}'");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add store (name may already exist).";
        }
    }

    [RelayCommand]
    private async Task SaveStoreAsync()
    {
        if (SelectedStore == null)
        {
            ErrorMessage = "Select a store to save changes.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedStore.GSTIN) && SelectedStore.GSTIN.Trim().Length != 15)
        {
            ErrorMessage = "GSTIN must be exactly 15 characters.";
            return;
        }

        if (string.Equals(SelectedStore.Name, "Main Store", StringComparison.OrdinalIgnoreCase) &&
            !SelectedStore.IsActive)
        {
            ErrorMessage = "The Main Store cannot be deactivated.";
            return;
        }

        var success = await _storeService.UpdateStoreAsync(SelectedStore);
        if (success)
        {
            ErrorMessage = string.Empty;
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                SelectedStore.Id, "StoreUpdated", "Store", SelectedStore.Name, $"Updated store '{SelectedStore.Name}'");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to save store.";
        }
    }

    [RelayCommand]
    private async Task DeleteStoreAsync()
    {
        if (SelectedStore == null)
        {
            ErrorMessage = "Select a store to delete.";
            return;
        }

        if (string.Equals(SelectedStore.Name, "Main Store", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "The Main Store cannot be deleted.";
            return;
        }

        var success = await _storeService.DeleteStoreAsync(SelectedStore.Id);
        if (success)
        {
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                null, "StoreDeleted", "Store", SelectedStore.Name, $"Deleted store '{SelectedStore.Name}'");
            SelectedStore = null;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to delete store.";
        }
    }
}
