using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-12: item-wise profit - pick a date range and ranking mode, then
/// generate per-product sales / COGS / profit rows with a total card.
/// </summary>
public partial class ItemProfitViewModel : BaseViewModel
{
    private readonly IItemProfitService _itemProfitService;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _topMode = "Top performers";

    [ObservableProperty]
    private int _count = 25;

    [ObservableProperty]
    private ObservableCollection<ItemProfitRow> _rows = new();

    [ObservableProperty]
    private decimal _totalProfit;

    [ObservableProperty]
    private decimal _avgMarginPercent;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> TopModes { get; } = new() { "Top performers", "Low margin items", "All" };

    public ItemProfitViewModel(IItemProfitService itemProfitService)
    {
        _itemProfitService = itemProfitService;
    }

    public async Task LoadDataAsync()
    {
        if (EndDate < StartDate)
        {
            ErrorMessage = "End date cannot be before the start date.";
            return;
        }

        IsLoading = true;
        try
        {
            var mode = TopMode switch
            {
                "Low margin items" => "Bottom",
                "All" => "All",
                _ => "Top"
            };
            var list = await _itemProfitService.GetItemProfitAsync(StartDate, EndDate, mode, Count);
            Rows = new ObservableCollection<ItemProfitRow>(list);
            TotalProfit = Rows.Sum(r => r.Profit);
            AvgMarginPercent = Rows.Any(r => r.Sales > 0m)
                ? Math.Round(Rows.Where(r => r.Sales > 0m).Average(r => r.ProfitMarginPercent), 1)
                : 0m;
            ErrorMessage = string.Empty;
            SuccessMessage = $"Generated profit report for {Rows.Count} item(s) " +
                             $"{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync() => await LoadDataAsync();
}
