using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-03: editor for the app-wide negative-stock policy, plus the products that
/// are already below zero under the current setting.
/// </summary>
public partial class StockPolicyViewModel : BaseViewModel
{
    private readonly IStockPolicyService _stockPolicyService;
    private readonly IProductService _productService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedMode = "Block";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedMode = "Block";

    [ObservableProperty]
    private string _lastChangedBy = "not changed yet";

    [ObservableProperty]
    private System.DateTime _lastChangedAt;

    [ObservableProperty]
    private ObservableCollection<Product> _negativeStockProducts = new();

    public System.Collections.Generic.List<string> Modes => _stockPolicyService.Modes;

    public string ModeDescription => StockPolicySetting.Describe(SelectedMode);

    public bool IsDirty => !string.Equals(SelectedMode, SavedMode, System.StringComparison.Ordinal);

    public StockPolicyViewModel(IStockPolicyService stockPolicyService, IProductService productService)
    {
        _stockPolicyService = stockPolicyService;
        _productService = productService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        var settings = await _stockPolicyService.GetSettingsAsync();
        SelectedMode = settings.NegativeStockMode;
        SavedMode = settings.NegativeStockMode;
        LastChangedBy = string.IsNullOrWhiteSpace(settings.UpdatedBy) ? "not changed yet" : settings.UpdatedBy;
        LastChangedAt = settings.UpdatedAt;
        StatusMessage = string.Empty;

        var products = await _productService.GetAllProductsAsync();
        NegativeStockProducts = new ObservableCollection<Product>(
            products.Where(p => p.StockQty < 0).OrderBy(p => p.StockQty).ToList());
    }

    [RelayCommand]
    private async Task SavePolicyAsync()
    {
        var (success, error) = await _stockPolicyService.SaveSettingsAsync(SelectedMode);
        if (success)
        {
            SavedMode = SelectedMode;
            StatusMessage = $"Negative-stock policy saved: {SelectedMode}.";
            await LoadDataAsync();
        }
        else
        {
            StatusMessage = error ?? "Failed to save the stock policy.";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();
}
