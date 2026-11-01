using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class SalesViewModel : BaseViewModel
{
    private readonly ISaleService _saleService;

    [ObservableProperty]
    private ObservableCollection<Sale> _sales = new();

    [ObservableProperty]
    private Sale _selectedSale = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private decimal _totalOutstanding;

    [ObservableProperty]
    private bool _isLoading;

    // New sale / credit entry
    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newInvoiceNumber = string.Empty;

    [ObservableProperty]
    private decimal _newAmount;

    [ObservableProperty]
    private decimal _newPaidAmount;

    [ObservableProperty]
    private string _newPaymentMode = "Cash";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public SalesViewModel(ISaleService saleService)
    {
        _saleService = saleService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var sales = await _saleService.GetAllSalesAsync();
        Sales = new ObservableCollection<Sale>(sales);
        TotalOutstanding = await _saleService.GetTotalOutstandingAsync();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RecordSaleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCustomerName) || NewAmount <= 0)
        {
            ErrorMessage = "Customer name and amount are required.";
            return;
        }

        var sale = new Sale
        {
            InvoiceNumber = NewInvoiceNumber,
            CustomerName = NewCustomerName,
            Amount = NewAmount,
            PaidAmount = NewPaidAmount,
            PaymentMode = NewPaymentMode,
            SaleDate = DateTime.UtcNow
        };

        var success = await _saleService.RecordSaleAsync(sale);
        if (success)
        {
            NewCustomerName = string.Empty;
            NewInvoiceNumber = string.Empty;
            NewAmount = 0;
            NewPaidAmount = 0;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to record sale.";
        }
    }

    [RelayCommand]
    private async Task RecordPaymentAsync()
    {
        if (SelectedSale == null) return;

        // Simplified - in a real app this would open a dialog
        var success = await _saleService.RecordPaymentAsync(SelectedSale.Id, SelectedSale.Amount - SelectedSale.PaidAmount);
        if (success)
        {
            await LoadDataAsync();
        }
    }

    public IEnumerable<Sale> FilteredSales =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Sales
            : Sales.Where(s => (s.CustomerName ?? string.Empty).Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                              || (s.InvoiceNumber ?? string.Empty).Contains(SearchText, System.StringComparison.OrdinalIgnoreCase));
}


