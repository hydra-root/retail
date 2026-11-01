using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-04: Low-stock alerts + STK-06 near-expiry alerts in one actionable view.
/// </summary>
public partial class StockAlertViewModel : BaseViewModel
{
    private readonly IProductService _productService;

    [ObservableProperty]
    private ObservableCollection<StockAlert> _alerts = new();

    [ObservableProperty]
    private ObservableCollection<StockAlert> _filteredAlerts = new();

    [ObservableProperty]
    private StockAlert _selectedAlert = null!;

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private int _outOfStockCount;

    [ObservableProperty]
    private int _lowStockCount;

    [ObservableProperty]
    private int _expiringSoonCount;

    [ObservableProperty]
    private int _expiredCount;

    public List<string> AlertFilters { get; } = new()
    {
        "All", "Out of Stock", "Low Stock", "Expiring Soon", "Expired"
    };

    /// <summary>Near-expiry window in days (STK-06 bucket).</summary>
    private const int ExpiryWindowDays = 30;

    public StockAlertViewModel(IProductService productService)
    {
        _productService = productService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        var products = await _productService.GetAllProductsAsync();
        var today = DateTime.Today;

        var alerts = new List<StockAlert>();

        foreach (var p in products)
        {
            // Stock side (STK-04): explicit threshold (MinStockQty > 0) or out-of-stock
            if (p.StockQty <= 0)
            {
                alerts.Add(new StockAlert
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    SKU = p.SKU,
                    Category = p.Category,
                    AlertType = "Out of Stock",
                    Severity = "Critical",
                    CurrentStock = p.StockQty,
                    MinStockQty = p.MinStockQty,
                    ReorderQty = Math.Max(10, p.MinStockQty * 2),
                    ExpiryDate = p.ExpiryDate
                });
            }
            else if (p.MinStockQty > 0 && p.StockQty <= p.MinStockQty)
            {
                alerts.Add(new StockAlert
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    SKU = p.SKU,
                    Category = p.Category,
                    AlertType = "Low Stock",
                    Severity = p.StockQty <= p.MinStockQty / 2 ? "Critical" : "Warning",
                    CurrentStock = p.StockQty,
                    MinStockQty = p.MinStockQty,
                    ReorderQty = Math.Max(10, p.MinStockQty * 2 - p.StockQty),
                    ExpiryDate = p.ExpiryDate
                });
            }

            // Expiry side (STK-06): expired or expiring within the window
            if (p.ExpiryDate.HasValue)
            {
                var days = (p.ExpiryDate.Value - today).Days;
                if (days < 0)
                {
                    alerts.Add(new StockAlert
                    {
                        ProductId = p.Id,
                        ProductName = p.Name,
                        SKU = p.SKU,
                        Category = p.Category,
                        AlertType = "Expired",
                        Severity = "Critical",
                        CurrentStock = p.StockQty,
                        MinStockQty = p.MinStockQty,
                        ExpiryDate = p.ExpiryDate,
                        DaysToExpiry = days
                    });
                }
                else if (days <= ExpiryWindowDays)
                {
                    alerts.Add(new StockAlert
                    {
                        ProductId = p.Id,
                        ProductName = p.Name,
                        SKU = p.SKU,
                        Category = p.Category,
                        AlertType = "Expiring Soon",
                        Severity = days <= ExpiryWindowDays / 3 ? "Critical" : "Warning",
                        CurrentStock = p.StockQty,
                        MinStockQty = p.MinStockQty,
                        ExpiryDate = p.ExpiryDate,
                        DaysToExpiry = days
                    });
                }
            }
        }

        // Urgent first, then by name
        alerts = alerts
            .OrderBy(a => a.Severity == "Critical" ? 0 : a.Severity == "Warning" ? 1 : 2)
            .ThenBy(a => a.ProductName)
            .ToList();

        OutOfStockCount = alerts.Count(a => a.AlertType == "Out of Stock");
        LowStockCount = alerts.Count(a => a.AlertType == "Low Stock");
        ExpiringSoonCount = alerts.Count(a => a.AlertType == "Expiring Soon");
        ExpiredCount = alerts.Count(a => a.AlertType == "Expired");

        Alerts = new ObservableCollection<StockAlert>(alerts);
        ApplyFilter();
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var list = SelectedFilter == "All"
            ? Alerts
            : Alerts.Where(a => a.AlertType == SelectedFilter);
        FilteredAlerts = new ObservableCollection<StockAlert>(list);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }
}
