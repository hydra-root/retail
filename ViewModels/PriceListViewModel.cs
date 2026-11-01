using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class PriceListViewModel : BaseViewModel
{
    private readonly IPriceListService _priceListService;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;

    [ObservableProperty]
    private ObservableCollection<PriceList> _priceLists = new();

    [ObservableProperty]
    private PriceList _selectedPriceList = null!;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newCustomerGroup = string.Empty;

    /// <summary>PAY-06: when set, this list prices apply to that customer ahead of any group list.</summary>
    [ObservableProperty]
    private Customer? _selectedNewCustomer;

    [ObservableProperty]
    private DateTime _newValidFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime? _newValidTo;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Product? _itemProduct;

    [ObservableProperty]
    private decimal _itemRate;

    [ObservableProperty]
    private decimal _itemDiscountPct;

    public PriceListViewModel(IPriceListService priceListService, IProductService productService, ICustomerService customerService)
    {
        _priceListService = priceListService;
        _productService = productService;
        _customerService = customerService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var lists = await _priceListService.GetAllPriceListsAsync();
        PriceLists = new ObservableCollection<PriceList>(lists);

        var products = await _productService.GetAllProductsAsync();
        Products = new ObservableCollection<Product>(products.OrderBy(p => p.Name));

        var customers = await _customerService.GetAllCustomersAsync();
        Customers = new ObservableCollection<Customer>(customers.OrderBy(c => c.Name));

        IsLoading = false;
    }

    /// <summary>PAY-06: picking a product seeds the rate box from the product master.</summary>
    partial void OnItemProductChanged(Product? value)
    {
        if (value != null && ItemRate == 0) ItemRate = value.Rate;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddPriceListAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Price list name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewCustomerGroup) && SelectedNewCustomer == null)
        {
            ErrorMessage = "Give the list a customer group or pick a customer, otherwise no bill can match it.";
            return;
        }

        var priceList = new PriceList
        {
            Name = NewName,
            CustomerGroup = NewCustomerGroup,
            CustomerId = SelectedNewCustomer?.Id,
            ValidFrom = NewValidFrom,
            ValidTo = NewValidTo,
            IsActive = true
        };

        var success = await _priceListService.AddPriceListAsync(priceList);
        if (success)
        {
            NewName = string.Empty;
            NewCustomerGroup = string.Empty;
            SelectedNewCustomer = null;
            NewValidFrom = System.DateTime.Today;
            NewValidTo = null;
            ErrorMessage = string.Empty;
            SuccessMessage = "Price list created. Select it in the grid to add product rates.";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add price list.";
        }
    }

    [RelayCommand]
    private async Task DeletePriceListAsync()
    {
        if (SelectedPriceList == null) return;

        var success = await _priceListService.DeletePriceListAsync(SelectedPriceList.Id);
        if (success)
        {
            SelectedPriceList = null!;
            SuccessMessage = "Price list deleted.";
            await LoadDataAsync();
        }
    }

    /// <summary>PAY-06: attach (or update) one product rate on the selected list.</summary>
    [RelayCommand]
    private async Task AddListItemAsync()
    {
        SuccessMessage = string.Empty;

        if (SelectedPriceList == null)
        {
            ErrorMessage = "Select a price list first.";
            return;
        }
        if (ItemProduct == null)
        {
            ErrorMessage = "Select the product to price.";
            return;
        }

        var (ok, error) = await _priceListService.AddItemAsync(
            SelectedPriceList.Id, ItemProduct.Id, ItemRate, ItemDiscountPct);

        if (!ok)
        {
            ErrorMessage = error ?? "Failed to save the list item.";
            return;
        }

        var keepId = SelectedPriceList.Id;
        ErrorMessage = string.Empty;
        SuccessMessage = $"{ItemProduct.Name} priced on {SelectedPriceList.Name}.";
        ItemProduct = null;
        ItemRate = 0;
        ItemDiscountPct = 0;
        await LoadDataAsync();
        SelectedPriceList = PriceLists.FirstOrDefault(p => p.Id == keepId)!;
    }

    /// <summary>PAY-06: drop a product from the selected list.</summary>
    [RelayCommand]
    private async Task RemoveListItemAsync(PriceListItem item)
    {
        if (SelectedPriceList == null || item == null) return;

        var (ok, error) = await _priceListService.RemoveItemAsync(SelectedPriceList.Id, item.ProductId);
        if (!ok)
        {
            ErrorMessage = error ?? "Failed to remove the list item.";
            return;
        }

        var keepId = SelectedPriceList.Id;
        ErrorMessage = string.Empty;
        SuccessMessage = $"Removed {item.Product?.Name ?? "item"} from {SelectedPriceList.Name}.";
        await LoadDataAsync();
        SelectedPriceList = PriceLists.FirstOrDefault(p => p.Id == keepId)!;
    }
}
