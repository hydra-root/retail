using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace RetailBillingApp.ViewModels;

public partial class ProductsViewModel : BaseViewModel
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IUnitService _unitService;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private Product _selectedProduct = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newSku = string.Empty;

    [ObservableProperty]
    private string _newBarcode = string.Empty;

    [ObservableProperty]
    private string _newCategory = string.Empty;

    [ObservableProperty]
    private string _newHsnCode = string.Empty;

    [ObservableProperty]
    private string _newBrand = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<Unit> _units = new();

    [ObservableProperty]
    private Unit? _selectedUnit;

    [ObservableProperty]
    private decimal _newRate;

    [ObservableProperty]
    private decimal _newTaxPercentage;

    [ObservableProperty]
    private int _newStockQty;

    [ObservableProperty]
    private int _newMinStockQty;

    [ObservableProperty]
    private string _newBatch = string.Empty;

    [ObservableProperty]
    private DateTime? _newExpiryDate;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ProductsViewModel(IProductService productService, ICategoryService categoryService, IUnitService unitService)
    {
        _productService = productService;
        _categoryService = categoryService;
        _unitService = unitService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var products = await _productService.GetAllProductsAsync();
        Products = new ObservableCollection<Product>(products);

        var categories = await _categoryService.GetAllCategoriesAsync();
        Categories = new ObservableCollection<Category>(categories);

        var units = await _unitService.GetAllUnitsAsync();
        Units = new ObservableCollection<Unit>(units);

        IsLoading = false;
    }

    [RelayCommand]
    private async Task AddProductAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Product name is required.";
            return;
        }

        var product = new Product
        {
            Name = NewName,
            SKU = NewSku,
            Barcode = NewBarcode,
            Category = SelectedCategory?.Name ?? NewCategory,
            CategoryId = SelectedCategory?.Id,
            Brand = string.IsNullOrWhiteSpace(NewBrand) ? null : NewBrand.Trim(),
            UnitId = SelectedUnit?.Id,
            HSNCode = NewHsnCode,
            Rate = NewRate,
            TaxPercentage = NewTaxPercentage,
            StockQty = NewStockQty,
            MinStockQty = NewMinStockQty,
            Batch = NewBatch,
            ExpiryDate = NewExpiryDate
        };

        var success = await _productService.AddProductAsync(product);
        if (success)
        {
            ClearForm();
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add product. SKU or Barcode may already exist.";
        }
    }

    [RelayCommand]
    private async Task DeleteProductAsync()
    {
        if (SelectedProduct == null) return;

        var success = await _productService.DeleteProductAsync(SelectedProduct.Id);
        if (success)
        {
            await LoadDataAsync();
            SelectedProduct = null!;
        }
    }

    private void ClearForm()
    {
        NewName = string.Empty;
        NewSku = string.Empty;
        NewBarcode = string.Empty;
        NewCategory = string.Empty;
        SelectedCategory = null;
        SelectedUnit = null;
        NewBrand = string.Empty;
        NewHsnCode = string.Empty;
        NewRate = 0;
        NewTaxPercentage = 0;
        NewStockQty = 0;
        NewMinStockQty = 0;
        NewBatch = string.Empty;
        NewExpiryDate = null;
        ErrorMessage = string.Empty;
    }

    public IEnumerable<Product> FilteredProducts =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Products
            : Products.Where(p => p.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                               || (p.SKU != null && p.SKU.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)));
}


