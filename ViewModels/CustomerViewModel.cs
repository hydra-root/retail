using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-04: Customer master - GSTIN (validated), state, group, credit days + limit.
/// </summary>
public partial class CustomerViewModel : BaseViewModel
{
    private readonly ICustomerService _customerService;

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Customer _selectedCustomer = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newMobile = string.Empty;

    [ObservableProperty]
    private string _newEmail = string.Empty;

    [ObservableProperty]
    private string _newAddress = string.Empty;

    [ObservableProperty]
    private string _newGstin = string.Empty;

    [ObservableProperty]
    private string _newState = string.Empty;

    [ObservableProperty]
    private string _newCustomerGroup = string.Empty;

    [ObservableProperty]
    private decimal _newCreditLimit;

    [ObservableProperty]
    private int _newCreditDays = 30;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    private static readonly List<string> IndianStates = new()
    {
        "Andhra Pradesh", "Assam", "Bihar", "Chandigarh", "Chhattisgarh", "Delhi", "Goa", "Gujarat",
        "Haryana", "Himachal Pradesh", "Jharkhand", "Karnataka", "Kerala", "Madhya Pradesh",
        "Maharashtra", "Manipur", "Meghalaya", "Odisha", "Puducherry", "Punjab", "Rajasthan",
        "Sikkim", "Tamil Nadu", "Telangana", "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal"
    };

    public List<string> States => IndianStates;

    public CustomerViewModel(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var customers = await _customerService.GetCustomersWithDuesAsync();
        Customers = new ObservableCollection<Customer>(customers);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Customer name is required.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(NewGstin) &&
            !TaxCalculationEngine.IsValidGSTIN(NewGstin.Trim()))
        {
            ErrorMessage = "Invalid GSTIN format (e.g. 07ABCDE1234F1Z5).";
            return;
        }

        var customer = new Customer
        {
            Name = NewName.Trim(),
            Mobile = string.IsNullOrWhiteSpace(NewMobile) ? null : NewMobile.Trim(),
            Email = string.IsNullOrWhiteSpace(NewEmail) ? null : NewEmail.Trim(),
            Address = string.IsNullOrWhiteSpace(NewAddress) ? null : NewAddress.Trim(),
            GSTIN = string.IsNullOrWhiteSpace(NewGstin) ? null : NewGstin.Trim(),
            State = string.IsNullOrWhiteSpace(NewState) ? null : NewState.Trim(),
            CustomerGroup = string.IsNullOrWhiteSpace(NewCustomerGroup) ? null : NewCustomerGroup.Trim(),
            CreditLimit = NewCreditLimit,
            CreditDays = NewCreditDays
        };

        var success = await _customerService.AddCustomerAsync(customer);
        if (success)
        {
            ClearForm();
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add customer.";
        }
    }

    [RelayCommand]
    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer == null) return;

        var success = await _customerService.DeleteCustomerAsync(SelectedCustomer.Id);
        if (success)
        {
            await LoadDataAsync();
            SelectedCustomer = null!;
        }
        else
        {
            ErrorMessage = "Failed to delete customer.";
        }
    }

    private void ClearForm()
    {
        NewName = string.Empty;
        NewMobile = string.Empty;
        NewEmail = string.Empty;
        NewAddress = string.Empty;
        NewGstin = string.Empty;
        NewState = string.Empty;
        NewCustomerGroup = string.Empty;
        NewCreditLimit = 0;
        NewCreditDays = 30;
        ErrorMessage = string.Empty;
    }

    public IEnumerable<Customer> FilteredCustomers =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Customers
            : Customers.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                                || (c.Mobile != null && c.Mobile.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                || (c.CustomerGroup != null && c.CustomerGroup.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
}
