using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-05: Batch-wise stock tracking. Batches are listed FEFO
/// (First Expired, First Out) so the oldest expiry shows first.
/// </summary>
public partial class BatchViewModel : BaseViewModel
{
    private readonly IProductService _productService;
    private readonly IBatchService _batchService;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private Product _selectedProduct = null!;

    [ObservableProperty]
    private ObservableCollection<ProductBatch> _batches = new();

    [ObservableProperty]
    private ProductBatch _selectedBatch = null!;

    [ObservableProperty]
    private string _newBatchNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _newManufacturingDate;

    [ObservableProperty]
    private DateTime? _newExpiryDate;

    [ObservableProperty]
    private decimal _newMRP;

    [ObservableProperty]
    private decimal _newPurchaseRate;

    [ObservableProperty]
    private int _newQuantity;

    [ObservableProperty]
    private string _newNotes = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public BatchViewModel(IProductService productService, IBatchService batchService)
    {
        _productService = productService;
        _batchService = batchService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var products = await _productService.GetAllProductsAsync();
        Products = new ObservableCollection<Product>(products);
        if (Batches.Count == 0 && SelectedProduct != null)
            await LoadBatchesAsync();
        IsLoading = false;
    }

    partial void OnSelectedProductChanged(Product value)
    {
        if (value != null)
            LoadBatchesAsync();
    }

    private async System.Threading.Tasks.Task LoadBatchesAsync()
    {
        if (SelectedProduct == null) return;
        var batches = await _batchService.GetBatchesForProductAsync(SelectedProduct.Id);
        Batches = new ObservableCollection<ProductBatch>(batches);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
        await LoadBatchesAsync();
    }

    [RelayCommand]
    private async Task AddBatchAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        if (SelectedProduct == null)
        {
            ErrorMessage = "Please select a product.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewBatchNumber))
        {
            ErrorMessage = "Please enter a batch number.";
            return;
        }

        if (NewExpiryDate.HasValue && NewManufacturingDate.HasValue
            && NewExpiryDate.Value < NewManufacturingDate.Value)
        {
            ErrorMessage = "Expiry date cannot be before the manufacturing date.";
            return;
        }

        if (NewQuantity <= 0)
        {
            ErrorMessage = "Quantity must be greater than zero.";
            return;
        }

        // Duplicate batch number check for this product
        var existing = await _batchService.GetBatchesForProductAsync(SelectedProduct.Id);
        if (existing.Any(b => b.BatchNumber.Equals(NewBatchNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"Batch '{NewBatchNumber}' already exists for {SelectedProduct.Name}.";
            return;
        }

        var batch = new ProductBatch
        {
            ProductId = SelectedProduct.Id,
            BatchNumber = NewBatchNumber.Trim(),
            ManufacturingDate = NewManufacturingDate,
            ExpiryDate = NewExpiryDate,
            MRP = NewMRP,
            PurchaseRate = NewPurchaseRate,
            Quantity = NewQuantity,
            Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim()
        };

        var ok = await _batchService.AddBatchAsync(batch);
        if (ok)
        {
            SuccessMessage = $"Batch {batch.BatchNumber} added ({batch.Quantity} units). Product stock updated.";
            NewBatchNumber = string.Empty;
            NewManufacturingDate = null;
            NewExpiryDate = null;
            NewMRP = 0;
            NewPurchaseRate = 0;
            NewQuantity = 0;
            NewNotes = string.Empty;
            await LoadBatchesAsync();
        }
        else
        {
            ErrorMessage = "Failed to add batch.";
        }
    }

    [RelayCommand]
    private async Task DeleteBatchAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        if (SelectedBatch == null)
        {
            ErrorMessage = "Please select a batch to delete.";
            return;
        }

        var ok = await _batchService.DeleteBatchAsync(SelectedBatch.Id);
        if (ok)
        {
            SuccessMessage = $"Batch {SelectedBatch.BatchNumber} removed. Product stock updated.";
            await LoadBatchesAsync();
        }
        else
        {
            ErrorMessage = "Failed to delete batch.";
        }
    }
}
