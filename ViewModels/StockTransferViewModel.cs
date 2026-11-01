using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-10: stock transfer between stores - dispatch in-transit, receive or
/// cancel per transfer, with KPI cards and a status-filtered grid.
/// </summary>
public partial class StockTransferViewModel : BaseViewModel
{
    private readonly IStockTransferService _transferService;
    private readonly IAuthService _authService;

    [ObservableProperty]
    private ObservableCollection<Store> _stores = new();

    [ObservableProperty]
    private Store? _fromStore = default!;

    [ObservableProperty]
    private Store? _toStore = default!;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private Product? _selectedProduct = default!;

    [ObservableProperty]
    private int _quantity;

    [ObservableProperty]
    private string _statusFilter = "InTransit";

    [ObservableProperty]
    private ObservableCollection<StockTransfer> _transfers = new();

    [ObservableProperty]
    private StockTransfer? _selectedTransfer = default!;

    [ObservableProperty]
    private int _inTransitCount;

    [ObservableProperty]
    private int _receivedCount;

    [ObservableProperty]
    private decimal _totalValueInTransit;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> StatusFilters { get; } = new() { "InTransit", "Received", "All", "Cancelled" };

    public StockTransferViewModel(IStockTransferService transferService, IAuthService authService)
    {
        _transferService = transferService;
        _authService = authService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            if (Stores.Count == 0)
                Stores = new ObservableCollection<Store>(await _transferService.GetStoreNamesAsync());

            var products = await _transferService.GetActiveProductsAsync();
            Products = new ObservableCollection<Product>(products);

            // Reset pickers if a selected entity was removed/deleted.
            if (FromStore != null && Stores.All(s => s.Id != FromStore.Id)) FromStore = null;
            if (ToStore != null && Stores.All(s => s.Id != ToStore.Id)) ToStore = null;
            if (SelectedProduct != null && Products.All(p => p.Id != SelectedProduct.Id)) SelectedProduct = null;

            await LoadTransfersAsync();

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

    private async System.Threading.Tasks.Task LoadTransfersAsync()
    {
        try
        {
            var list = await _transferService.GetTransfersAsync(StatusFilter);
            Transfers = new ObservableCollection<StockTransfer>(list);
            SelectedTransfer = null;
            InTransitCount = list.Count(t => t.Status == "InTransit");
            ReceivedCount = list.Count(t => t.Status == "Received");
            // "Total value in transit" always reflects ALL in-transit transfers, not just the filtered view.
            var inTransitAll = StatusFilter == "InTransit"
                ? list
                : await _transferService.GetTransfersAsync("InTransit");
            TotalValueInTransit = inTransitAll.Where(t => t.Status == "InTransit").Sum(t => t.TransferValue);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnStatusFilterChanged(string value) => _ = LoadTransfersAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task CreateTransferAsync()
    {
        SuccessMessage = string.Empty;
        if (FromStore == null) { ErrorMessage = "Please select a source store."; return; }
        if (ToStore == null) { ErrorMessage = "Please select a destination store."; return; }
        if (SelectedProduct == null) { ErrorMessage = "Please select a product."; return; }
        if (Quantity <= 0) { ErrorMessage = "Transfer quantity must be greater than zero."; return; }

        var (ok, error) = await _transferService.CreateTransferAsync(
            FromStore.Id, ToStore.Id, SelectedProduct.Id, Quantity);

        if (ok)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Transfer created: {Quantity} x {SelectedProduct.Name} {FromStore.Name} → {ToStore.Name}. Status: In-Transit.";
            if (_transferService.StockPolicyWarnings.Count > 0)
                SuccessMessage += " Warning: " + string.Join(" ", _transferService.StockPolicyWarnings);
            Quantity = 0;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = error ?? "Failed to create transfer.";
        }
    }

    [RelayCommand]
    private async Task ReceiveAsync()
    {
        SuccessMessage = string.Empty;
        if (SelectedTransfer == null) { ErrorMessage = "Select a transfer in the grid first."; return; }
        if (SelectedTransfer.Status != "InTransit")
        {
            ErrorMessage = "Only in-transit transfers can be received.";
            return;
        }

        var (ok, error) = await _transferService.ReceiveTransferAsync(SelectedTransfer.Id);
        if (ok)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Transfer {SelectedTransfer.TransferNumber} received at destination.";
            await LoadTransfersAsync();
        }
        else
        {
            ErrorMessage = error ?? "Failed to receive transfer.";
        }
    }

    [RelayCommand]
    private async Task CancelTransferAsync()
    {
        SuccessMessage = string.Empty;
        if (SelectedTransfer == null) { ErrorMessage = "Select a transfer in the grid first."; return; }
        if (SelectedTransfer.Status != "InTransit")
        {
            ErrorMessage = "Only in-transit transfers can be cancelled.";
            return;
        }

        var username = _authService.CurrentUser?.Username ?? "admin";

        var (ok, error) = await _transferService.CancelTransferAsync(SelectedTransfer.Id, username);
        if (ok)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Transfer {SelectedTransfer.TransferNumber} cancelled; stock returned to source.";
            await LoadTransfersAsync();
        }
        else
        {
            ErrorMessage = error ?? "Failed to cancel transfer.";
        }
    }
}
