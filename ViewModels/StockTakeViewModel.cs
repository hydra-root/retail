using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-14: physical stock verification — stocktake sheet, variance report
/// and posting variances as stock adjustments.
/// </summary>
public partial class StockTakeViewModel : BaseViewModel
{
    private readonly IStockTakeService _stockTakeService;

    [ObservableProperty]
    private ObservableCollection<StockTake> _stockTakes = new();

    [ObservableProperty]
    private StockTake? _selectedTake;

    [ObservableProperty]
    private ObservableCollection<StockTakeItem> _currentItems = new();

    [ObservableProperty]
    private StockTakeItem? _selectedItem;

    // New stocktake form
    [ObservableProperty]
    private string _newStockTakeNotes = string.Empty;

    [ObservableProperty]
    private int? _newStockTakeStoreId;

    // Count entry form
    [ObservableProperty]
    private int _countedQtyEntry;

    // Variance summary cards
    [ObservableProperty]
    private int _itemsCounted;

    [ObservableProperty]
    private int _varianceQty;

    [ObservableProperty]
    private decimal _varianceValueCard;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public StockTakeViewModel(IStockTakeService stockTakeService)
    {
        _stockTakeService = stockTakeService;
    }

    /// <summary>Current take is open for counting / completion.</summary>
    public bool IsCurrentTakeOpen => SelectedTake != null && SelectedTake.Status == "Open";

    public bool HasCurrentTake => SelectedTake != null;

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var takes = await _stockTakeService.GetAllStockTakesAsync();
            StockTakes = new ObservableCollection<StockTake>(takes);

            // Refresh the open take's sheet if one is selected
            if (SelectedTake != null)
            {
                var refreshed = takes.FirstOrDefault(t => t.Id == SelectedTake.Id);
                if (refreshed != null)
                {
                    await LoadTakeSheetAsync(refreshed.Id);
                }
            }

            ErrorMessage = string.Empty;
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

    private async System.Threading.Tasks.Task LoadTakeSheetAsync(int takeId)
    {
        var take = await _stockTakeService.GetStockTakeWithItemsAsync(takeId);
        if (take == null) return;

        SelectedTake = take;
        CurrentItems = new ObservableCollection<StockTakeItem>(take.Items);

        ItemsCounted = take.Items.Count(i => i.CountedQty.HasValue);
        VarianceQty = take.Items.Where(i => i.CountedQty.HasValue).Sum(i => i.Variance);
        VarianceValueCard = take.Items.Sum(i => i.VarianceValue);

        OnPropertyChanged(nameof(IsCurrentTakeOpen));
        OnPropertyChanged(nameof(HasCurrentTake));
    }

    partial void OnSelectedTakeChanged(StockTake? value)
    {
        if (value != null)
            _ = LoadTakeSheetAsync(value.Id);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task CreateStockTakeAsync()
    {
        try
        {
            var newId = await _stockTakeService.CreateStockTakeAsync(NewStockTakeNotes, NewStockTakeStoreId);
            NewStockTakeNotes = string.Empty;
            NewStockTakeStoreId = null;

            SuccessMessage = "New stocktake created with a snapshot of current system quantities. Count the items, then Complete.";
            ErrorMessage = string.Empty;

            await LoadDataAsync();
            await LoadTakeSheetAsync(newId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RecordCountAsync()
    {
        ErrorMessage = string.Empty;

        if (SelectedTake == null)
        {
            ErrorMessage = "Select or create a stocktake first.";
            return;
        }

        if (SelectedTake.Status != "Open")
        {
            ErrorMessage = "This stocktake is already completed.";
            return;
        }

        if (SelectedItem == null)
        {
            ErrorMessage = "Select a product row to record its count.";
            return;
        }

        var success = await _stockTakeService.RecordCountAsync(SelectedTake.Id, SelectedItem.ProductId, CountedQtyEntry);
        if (!success)
        {
            ErrorMessage = "Failed to record the count.";
            return;
        }

        SuccessMessage = $"Count recorded for {SelectedItem.ProductName}: {CountedQtyEntry} (system {SelectedItem.SystemQty}, variance {(CountedQtyEntry - SelectedItem.SystemQty):+0;-0;0}).";
        await LoadTakeSheetAsync(SelectedTake.Id);

        // Keep the row selected and ready for the next entry
        if (SelectedItem != null)
        {
            var productId = SelectedItem.ProductId;
            SelectedItem = CurrentItems.FirstOrDefault(i => i.ProductId == productId) ?? SelectedItem;
        }
        CountedQtyEntry = 0;
    }

    [RelayCommand]
    private async Task CompleteStockTakeAsync()
    {
        ErrorMessage = string.Empty;

        if (SelectedTake == null)
        {
            ErrorMessage = "Select a stocktake first.";
            return;
        }

        if (SelectedTake.Status != "Open")
        {
            ErrorMessage = "This stocktake is already completed.";
            return;
        }

        var (success, adjustedCount, totalVarianceValue) =
            await _stockTakeService.CompleteStockTakeAsync(SelectedTake.Id, "admin");

        if (!success)
        {
            ErrorMessage = _stockTakeService.LastValidationError
                ?? "Failed to complete the stocktake. No variances were posted.";
            return;
        }

        SuccessMessage = $"Stocktake {SelectedTake.StockTakeNumber} completed. {adjustedCount} item(s) adjusted, total variance value {totalVarianceValue:C}.";
        await LoadTakeSheetAsync(SelectedTake.Id);
        var takes = await _stockTakeService.GetAllStockTakesAsync();
        StockTakes = new ObservableCollection<StockTake>(takes);
    }
}
