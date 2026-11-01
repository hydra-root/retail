using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-07: customer ledger with running balance + printable statement.
/// </summary>
public partial class CustomerLedgerViewModel : BaseViewModel
{
    private readonly IReportService _reportService;
    private readonly ICustomerService _customerService;

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private CustomerLedgerReport _ledgerRow = new();

    [ObservableProperty]
    private ObservableCollection<CustomerLedgerRow> _rows = new();

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public CustomerLedgerViewModel(IReportService reportService, ICustomerService customerService)
    {
        _reportService = reportService;
        _customerService = customerService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var customers = await _customerService.GetAllCustomersAsync();
        Customers = new ObservableCollection<Customer>(customers);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (SelectedCustomer == null)
        {
            ErrorMessage = "Select a customer first.";
            return;
        }

        try
        {
            var report = await _reportService.GetCustomerLedgerAsync(SelectedCustomer.Id, StartDate, EndDate);
            LedgerRow = report;
            Rows = new ObservableCollection<CustomerLedgerRow>(report.Rows);
            ErrorMessage = string.Empty;
            SuccessMessage = $"{report.Rows.Count} transactions, closing balance {report.ClosingBalance:C}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>PAY-07: print (text file on Desktop) the statement of account.</summary>
    [RelayCommand]
    private async Task PrintStatementAsync()
    {
        if (SelectedCustomer == null)
        {
            ErrorMessage = "Select a customer first.";
            return;
        }

        var path = await _reportService.PrintCustomerStatementAsync(SelectedCustomer.Id, StartDate, EndDate);
        if (path != null)
        {
            SuccessMessage = $"Statement saved to {path}";
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = "Failed to build the statement.";
        }
    }
}
