using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// USR-03: store-wise pricing management screen. Pick a store (and an optional buyer scope:
/// a customer or a customer group, empty = everyone), and the grid shows for every catalogue
/// product what the global chain charges vs what a bill at this store would pay and which tier
/// won. Overrides are written only through IStorePricingService; the edit card targets the
/// selected grid row, and the store-wide card handles bulk copy/clear for the chosen scope.
/// </summary>
public partial class StorePricingViewModel : BaseViewModel
{
    private readonly IStorePricingService _storePricingService;
    private readonly IStoreService _storeService;
    private readonly ICustomerService _customerService;

    // ---- pickers ----
    [ObservableProperty]
    private ObservableCollection<Store> _stores = new();

    [ObservableProperty]
    private Store? _selectedStore;

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private string? _selectedCustomerGroup;

    // ---- grid ----
    [ObservableProperty]
    private ObservableCollection<StorePriceRow> _rows = new();

    [ObservableProperty]
    private StorePriceRow? _selectedRow;

    // ---- edit override card ----
    [ObservableProperty]
    private decimal _editRate;

    [ObservableProperty]
    private decimal _editDiscountPct;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    // ---- headline + messages ----
    [ObservableProperty]
    private string _countText = "Select a store.";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    public StorePricingViewModel(IStorePricingService storePricingService,
        IStoreService storeService, ICustomerService customerService)
    {
        _storePricingService = storePricingService;
        _storeService = storeService;
        _customerService = customerService;
    }

    /// <summary>Fires the grid on every scope change, exactly like the house FilterChanged pattern.</summary>
    partial void OnSelectedStoreChanged(Store? value) => _ = GridAsync();
    partial void OnSelectedCustomerChanged(Customer? value) => _ = GridAsync();
    partial void OnSelectedCustomerGroupChanged(string? value) => _ = GridAsync();

    /// <summary>Prefill the edit card from the selected row so an existing override can be tweaked.</summary>
    partial void OnSelectedRowChanged(StorePriceRow? value)
    {
        EditRate = value?.StoreRate ?? value?.GlobalNet ?? 0;
        EditDiscountPct = value?.StoreDiscountPct ?? 0;
        EditNotes = string.Empty;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        try
        {
            var stores = await _storeService.GetAllStoresAsync();
            Stores = new ObservableCollection<Store>(stores);

            var customers = await _customerService.GetAllCustomersAsync();
            Customers = new ObservableCollection<Customer>(customers);

            // Selecting the first store raises OnSelectedStoreChanged, which runs the grid;
            // a re-load with an existing selection (or no store at all) runs it explicitly.
            if (SelectedStore == null && Stores.Count > 0)
                SelectedStore = Stores[0];
            else
                await GridAsync();
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not load stores/customers: {ex.Message}", null);
        }
    }

    // ---- reload command (also the toolbar button) ----

    [RelayCommand]
    private async Task GridAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedStore == null)
        {
            Rows = new ObservableCollection<StorePriceRow>();
            CountText = "Select a store.";
            return;
        }

        try
        {
            var result = await _storePricingService.GetStorePricingGridAsync(
                SelectedStore.Id, SelectedCustomer?.Id, GroupOrNull());

            Rows = new ObservableCollection<StorePriceRow>(result);
            await RefreshCountAsync();
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not load the store pricing grid: {ex.Message}", null);
        }
    }

    // ---- edit override commands ----

    [RelayCommand]
    private async Task SaveOverrideAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedStore == null)
        {
            ShowMessage("Select a store first.", null);
            return;
        }

        if (SelectedRow == null)
        {
            ShowMessage("Select a product in the grid first.", null);
            return;
        }

        if (EditRate <= 0)
        {
            ShowMessage("Enter a store rate greater than zero.", null);
            return;
        }

        var result = await _storePricingService.SetOverrideAsync(
            SelectedStore.Id, SelectedRow.ProductId, EditRate, EditDiscountPct,
            SelectedCustomer?.Id, GroupOrNull(),
            string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim());

        if (result.Success)
        {
            ShowMessage(null, $"Store price saved for {SelectedRow.ProductName}.");
            await GridAsync();
        }
        else
        {
            ShowMessage(result.Error ?? "Could not save the store price.", null);
        }
    }

    [RelayCommand]
    private async Task ClearOverrideAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedRow?.OverrideId is not int overrideId)
        {
            ShowMessage("Select a row that has a store override.", null);
            return;
        }

        var result = await _storePricingService.ClearOverrideAsync(overrideId);
        if (result.Success)
        {
            ShowMessage(null, $"Store override cleared for {SelectedRow.ProductName}.");
            await GridAsync();
        }
        else
        {
            ShowMessage(result.Error ?? "Could not clear the store override.", null);
        }
    }

    // ---- store-wide (bulk) commands ----

    [RelayCommand]
    private async Task ClearStoreAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedStore == null)
        {
            ShowMessage("Select a store first.", null);
            return;
        }

        var result = await _storePricingService.ClearStoreOverridesAsync(
            SelectedStore.Id, SelectedCustomer?.Id, GroupOrNull());

        if (string.IsNullOrEmpty(result.Error))
        {
            ShowMessage(null, $"Cleared {result.ClearedCount} override row(s) for this scope.");
            await GridAsync();
        }
        else
        {
            ShowMessage(result.Error, null);
        }
    }

    [RelayCommand]
    private async Task CopyGlobalAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedStore == null)
        {
            ShowMessage("Select a store first.", null);
            return;
        }

        var result = await _storePricingService.CopyGlobalRatesAsync(SelectedStore.Id, null);
        if (string.IsNullOrEmpty(result.Error))
        {
            ShowMessage(null, $"Copied {result.CopiedCount} global rate(s) into {SelectedStore.Name}.");
            await GridAsync();
        }
        else
        {
            ShowMessage(result.Error, null);
        }
    }

    // ---- helpers ----

    /// <summary>The buyer scope the pickers describe: a customer rows carry no group, so when a
    /// customer is chosen the group box is ignored — same normalisation the service applies.</summary>
    private string? GroupOrNull() =>
        SelectedCustomer?.Id == null && !string.IsNullOrWhiteSpace(SelectedCustomerGroup)
            ? SelectedCustomerGroup.Trim()
            : null;

    private async Task RefreshCountAsync()
    {
        if (SelectedStore == null) return;
        try
        {
            var count = await _storePricingService.CountOverridesAsync(
                SelectedStore.Id, SelectedCustomer?.Id, GroupOrNull());
            CountText = $"{count} override row(s) on {SelectedStore.Name} for this scope.";
        }
        catch
        {
            CountText = string.Empty;
        }
    }

    private void ShowMessage(string? error, string? success)
    {
        ErrorMessage = error ?? string.Empty;
        SuccessMessage = success ?? string.Empty;
    }
}
