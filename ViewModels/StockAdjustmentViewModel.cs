using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class StockAdjustmentViewModel : BaseViewModel
{
    private readonly IProductService _productService;
    private readonly ICriticalChangeGuard _criticalChangeGuard;
    private readonly IOfflineQueueService _offlineQueue;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private Product _selectedProduct = null!;

    [ObservableProperty]
    private int _adjustmentQty;

    [ObservableProperty]
    private string _adjustmentReason = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>STK-07: history of recent adjustments.</summary>
    [ObservableProperty]
    private ObservableCollection<AdjustmentHistoryRow> _adjustmentHistory = new();

    public List<string> AdjustmentReasons { get; } = new()
    {
        "Damage",
        "Loss",
        "Expiry",
        "Found",
        "Opening"
    };

    public StockAdjustmentViewModel(IProductService productService,
        ICriticalChangeGuard criticalChangeGuard, IOfflineQueueService offlineQueue)
    {
        _productService = productService;
        _criticalChangeGuard = criticalChangeGuard;
        _offlineQueue = offlineQueue;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var products = await _productService.GetAllProductsAsync();
        Products = new ObservableCollection<Product>(products);
        AdjustmentHistory = new ObservableCollection<AdjustmentHistoryRow>(
            await _productService.GetAdjustmentHistoryAsync());
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    /// <summary>
    /// SEC-05: the separate critical-change passcode. Returns true when the action may proceed.
    /// </summary>
    private async Task<bool> ConfirmCriticalChangeAsync(CriticalChangeKind kind, string action, string? target = null)
    {
        if (!await _criticalChangeGuard.IsRequiredAsync(kind))
            return true;                       // master switch off, or this gate not armed

        string? refusal = null;
        DateTime? lockedUntilUtc = null;
        int remaining = 0;

        while (true)
        {
            var prompt = Views.CriticalChangeDialog.Show(
                action, CriticalChangeSetting.DescribeGate(kind), refusal, lockedUntilUtc, remaining);
            if (prompt == null) return false;  // operator cancelled

            var outcome = await _criticalChangeGuard.VerifyAsync(kind, prompt.Passcode, prompt.Reason, target);
            if (outcome.Allowed)
            {
                ErrorMessage = string.Empty;
                return true;
            }

            refusal = outcome.Message;
            lockedUntilUtc = outcome.LockedUntilUtc;
            remaining = outcome.RemainingAttempts;
        }
    }

    [RelayCommand]
    private async Task AdjustStockAsync()
    {
        if (SelectedProduct == null)
        {
            ErrorMessage = "Please select a product.";
            return;
        }

        if (AdjustmentQty == 0)
        {
            ErrorMessage = "Adjustment quantity cannot be zero.";
            return;
        }

        if (string.IsNullOrWhiteSpace(AdjustmentReason))
        {
            ErrorMessage = "Please select a reason.";
            return;
        }

        // SEC-05: a manual adjustment changes inventory value with no purchase/sale document.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.StockAdjustment,
                $"Adjust stock of {SelectedProduct.Name} ({SelectedProduct.SKU}) by {AdjustmentQty:+0;-0} — " +
                $"on hand {SelectedProduct.StockQty}, reason {AdjustmentReason}",
                SelectedProduct.SKU))
            return;

        var success = await _productService.AdjustStockAsync(
            SelectedProduct.Id,
            AdjustmentQty,
            AdjustmentReason);

        if (success)
        {
            // STK-11: the movement goes to HQ through the outbox.
            await _offlineQueue.EnqueueAsync(PendingChange.EntityStockTransaction, PendingChange.ActionCreate,
                SelectedProduct.SKU ?? SelectedProduct.Id.ToString(),
                new { ProductId = SelectedProduct.Id, SelectedProduct.Name, SelectedProduct.SKU, QuantityChange = AdjustmentQty, Reason = AdjustmentReason, TransactionType = "Adjustment" });

            ErrorMessage = string.Empty;
            var newQty = SelectedProduct.StockQty + AdjustmentQty;
            SuccessMessage = $"Stock adjusted by {AdjustmentQty:+0;-0} for {SelectedProduct.Name}. New stock: {newQty} ({AdjustmentReason}).";
            if (_productService.StockPolicyWarnings.Count > 0)
                SuccessMessage += " Warning: " + string.Join(" ", _productService.StockPolicyWarnings);
            await LoadDataAsync();
            AdjustmentQty = 0;
            AdjustmentReason = string.Empty;
            SelectedProduct = null!;
        }
        else
        {
            ErrorMessage = _productService.LastValidationError ?? "Failed to adjust stock.";
        }
    }
}
