using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-09: stock valuation by Weighted Average or FIFO, with totals and CSV export.
/// </summary>
public partial class StockValuationViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<StockValuationRow> _rows = new();

    [ObservableProperty]
    private string _method = "Weighted Average";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private decimal _totalStockValue;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Methods { get; } = new() { "Weighted Average", "FIFO" };

    public StockValuationViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _reportService.GetStockValuationAsync(Method);
            Rows = new ObservableCollection<StockValuationRow>(list);
            TotalStockValue = Rows.Sum(r => r.StockValue);
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

    partial void OnMethodChanged(string value) => _ = LoadDataAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    public IEnumerable<StockValuationRow> FilteredRows =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Rows
            : Rows.Where(r => r.ProductName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                           || r.SKU.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = $"StockValuation_{Method.Replace(" ", "")}_{DateTime.Today:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Stock Valuation ({Method}),{DateTime.Today:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine("Product,SKU,Stock Qty,Unit Cost,Stock Value,Basis");
            foreach (var r in FilteredRows)
                sb.AppendLine($"{r.ProductName},{r.SKU},{r.StockQty},{r.UnitCost:F2},{r.StockValue:F2},{r.Basis}");
            sb.AppendLine($"TOTAL,,{FilteredRows.Sum(r => r.StockQty)},{string.Empty},{TotalStockValue:F2},");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }
}
