using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-07: stock audit sheet - book vs stock ledger vs last physical count, with exception filters and CSV export.
/// </summary>
public partial class StockAuditReportViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<StockAuditRow> _rows = new();

    [ObservableProperty]
    private StockAuditReport _summary = new();

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filter = "Exceptions only";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Filters { get; } = new()
    {
        "Exceptions only",
        "All products",
        "Unrecorded Stock",
        "Negative Stock",
        "Unposted Count",
        "Below Minimum",
        "Expired Batch",
        "No Movement"
    };

    public StockAuditReportViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var report = await _reportService.GetStockAuditReportAsync(StartDate, EndDate);
            Summary = report;
            Rows = new ObservableCollection<StockAuditRow>(report.Rows);
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

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredRows));

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredRows));

    public IEnumerable<StockAuditRow> FilteredRows
    {
        get
        {
            var query = Rows.AsEnumerable();

            if (Filter == "Exceptions only") query = query.Where(r => r.HasException);
            else if (Filter != "All products") query = query.Where(r => r.Exceptions.Contains(Filter));

            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(r => r.ProductName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                                       || r.SKU.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                                       || r.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            return query.ToList();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var rows = FilteredRows.ToList();
            var fileName = $"StockAudit_{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Stock Audit Report,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine($"Products audited,{Summary.ProductCount},Clean,{Summary.CleanCount}");
            sb.AppendLine($"Unrecorded stock rows,{Summary.MismatchCount},Net value,{Summary.MismatchValue:F2}");
            sb.AppendLine($"Negative stock,{Summary.NegativeStockCount},Unposted counts,{Summary.UnpostedCountCount}");
            sb.AppendLine($"No-movement value,{Summary.NoMovementValue:F2},Total stock value,{Summary.TotalStockValue:F2}");
            sb.AppendLine();
            sb.AppendLine("Product,SKU,Category,Opening,In,Out,Closing,Book Qty,Ledger Qty,Unrecorded Qty,Unrecorded Value,Last Count,Counted Qty,Count Variance,Last Movement,Value,Flag");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(',',
                    Escape(r.ProductName), Escape(r.SKU), Escape(r.Category),
                    r.OpeningQty.ToString("F2"), r.QtyIn.ToString("F2"), r.QtyOut.ToString("F2"),
                    r.ClosingQty.ToString("F2"), r.BookQty.ToString("F2"), r.LedgerQty.ToString("F2"),
                    r.LedgerVariance.ToString("F2"), r.VarianceValue.ToString("F2"),
                    r.LastCountDate?.ToString("yyyy-MM-dd") ?? "-",
                    r.LastCountedQty?.ToString() ?? "-",
                    r.LastCountedQty.HasValue ? r.CountVariance.ToString("F2") : "-",
                    r.LastMovementDate?.ToString("yyyy-MM-dd") ?? "never",
                    r.StockValue.ToString("F2"),
                    Escape(r.ExceptionText)));
            }

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported {rows.Count} row(s) to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
