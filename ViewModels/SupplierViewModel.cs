using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class SupplierViewModel : BaseViewModel
{
    private readonly ISupplierService _supplierService;

    [ObservableProperty]
    private ObservableCollection<Supplier> _suppliers = new();

    [ObservableProperty]
    private Supplier _selectedSupplier = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newMobile = string.Empty;

    [ObservableProperty]
    private string _newEmail = string.Empty;

    [ObservableProperty]
    private string _newGstin = string.Empty;

    [ObservableProperty]
    private string _newAddress = string.Empty;

    [ObservableProperty]
    private decimal _newOpeningBalance;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public SupplierViewModel(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var suppliers = await _supplierService.GetAllSuppliersAsync();
        Suppliers = new ObservableCollection<Supplier>(suppliers);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Supplier name is required.";
            return;
        }

        var supplier = new Supplier
        {
            Name = NewName,
            Mobile = NewMobile,
            Email = NewEmail,
            GSTIN = NewGstin,
            Address = NewAddress,
            OpeningBalance = NewOpeningBalance
        };

        var success = await _supplierService.AddSupplierAsync(supplier);
        if (success)
        {
            ClearForm();
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add supplier.";
        }
    }

    [RelayCommand]
    private async Task DeleteSupplierAsync()
    {
        if (SelectedSupplier == null) return;

        var success = await _supplierService.DeleteSupplierAsync(SelectedSupplier.Id);
        if (success)
        {
            await LoadDataAsync();
            SelectedSupplier = null!;
        }
    }

    private void ClearForm()
    {
        NewName = string.Empty;
        NewMobile = string.Empty;
        NewEmail = string.Empty;
        NewGstin = string.Empty;
        NewAddress = string.Empty;
        NewOpeningBalance = 0;
        ErrorMessage = string.Empty;
    }

    public IEnumerable<Supplier> FilteredSuppliers =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Suppliers
            : Suppliers.Where(s => s.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                               || (s.Mobile != null && s.Mobile.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)));
}
