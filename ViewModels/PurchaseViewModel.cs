using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class PurchaseViewModel : BaseViewModel
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly IProductService _productService;

    [ObservableProperty]
    private ObservableCollection<Purchase> _purchases = new();

    [ObservableProperty]
    private Purchase _selectedPurchase = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newSupplierName = string.Empty;

    [ObservableProperty]
    private string _newPaymentMode = "Cash";

    [ObservableProperty]
    private decimal _newPaidAmount;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // New purchase form
    [ObservableProperty]
    private ObservableCollection<PurchaseItem> _newItems = new();

    [ObservableProperty]
    private string _newProductName = string.Empty;

    [ObservableProperty]
    private int _newProductQty = 1;

    [ObservableProperty]
    private decimal _newProductRate;

    [ObservableProperty]
    private decimal _newProductTax;

    [ObservableProperty]
    private string _newBatch = string.Empty;

    [ObservableProperty]
    private System.DateTime? _newExpiryDate;

    [ObservableProperty]
    private decimal _newSubTotal;

    [ObservableProperty]
    private decimal _newTaxAmount;

    [ObservableProperty]
    private decimal _newTotalAmount;

    public List<string> PaymentModes { get; } = new() { "Cash", "Card", "UPI", "Credit" };

    public PurchaseViewModel(IPurchaseService purchaseService, ISupplierService supplierService, IProductService productService)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _productService = productService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var purchases = await _purchaseService.GetAllPurchasesAsync();
        Purchases = new ObservableCollection<Purchase>(purchases);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private void AddItemToPurchase()
    {
        if (string.IsNullOrWhiteSpace(NewProductName)) return;

        var item = new PurchaseItem
        {
            ProductName = NewProductName,
            Quantity = NewProductQty,
            Rate = NewProductRate,
            TaxPercentage = NewProductTax,
            Amount = NewProductQty * NewProductRate,
            TaxAmount = (NewProductQty * NewProductRate) * (NewProductTax / 100m),
            Batch = NewBatch,
            ExpiryDate = NewExpiryDate
        };

        NewItems.Add(item);
        RecalculateTotals();

        NewProductName = string.Empty;
        NewProductQty = 1;
        NewProductRate = 0;
        NewProductTax = 0;
        NewBatch = string.Empty;
        NewExpiryDate = null;
    }

    [RelayCommand]
    private void RemoveItemFromPurchase(PurchaseItem item)
    {
        NewItems.Remove(item);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        NewSubTotal = NewItems.Sum(i => i.Amount);
        NewTaxAmount = NewItems.Sum(i => i.TaxAmount);
        NewTotalAmount = NewSubTotal + NewTaxAmount;
    }

    [RelayCommand]
    private async Task CreatePurchaseAsync()
    {
        if (NewItems.Count == 0)
        {
            ErrorMessage = "Please add at least one item.";
            return;
        }

        var purchaseNumber = await _purchaseService.GenerateNextPurchaseNumberAsync();

        var purchase = new Purchase
        {
            PurchaseNumber = purchaseNumber,
            PurchaseDate = System.DateTime.UtcNow,
            SupplierName = NewSupplierName,
            PaymentMode = NewPaymentMode,
            Items = NewItems.ToList(),
            PaidAmount = NewPaidAmount
        };

        var success = await _purchaseService.CreatePurchaseAsync(purchase);
        if (success)
        {
            NewItems.Clear();
            NewSupplierName = string.Empty;
            NewPaidAmount = 0;
            NewSubTotal = 0;
            NewTaxAmount = 0;
            NewTotalAmount = 0;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to create purchase.";
        }
    }

    public IEnumerable<Purchase> FilteredPurchases =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Purchases
            : Purchases.Where(p => p.PurchaseNumber.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                               || (p.SupplierName != null && p.SupplierName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)));
}
