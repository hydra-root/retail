using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-10: multi-store comparison - store-wise sales, margin, ranking.
/// </summary>
public partial class StoreComparisonViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<StoreComparisonRow> _rows = new();

    [ObservableProperty]
    private decimal _totalSales;

    [ObservableProperty]
    private decimal _totalGrossProfit;

    [ObservableProperty]
    private string _bestStore = "-";

    [ObservableProperty]
    private string _rangeSummary = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public StoreComparisonViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync() => await GenerateAsync();

    [RelayCommand]
    private async Task GenerateAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _reportService.GetStoreComparisonAsync(StartDate, EndDate);
            Rows = new ObservableCollection<StoreComparisonRow>(list);
            TotalSales = list.Sum(r => r.Sales);
            TotalGrossProfit = list.Sum(r => r.GrossProfit);
            var best = list.FirstOrDefault(r => r.Rank == 1);
            BestStore = best == null ? "-" : $"{best.StoreName} ({best.SalesSharePercent:P0})";

            var active = list.Count(r => r.Rank > 0);
            var overallMargin = TotalSales > 0m ? TotalGrossProfit * 100m / TotalSales : 0m;
            RangeSummary = $"{list.Count} stores ({active} active), overall margin {overallMargin:F1}%";
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

    /// <summary>RPT-10: export the comparison to CSV on Desktop.</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"StoreComparison_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Multi-Store Comparison,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine($"Total Sales,{TotalSales:F2}");
            sb.AppendLine();
            sb.AppendLine("Rank,Store,Location,Invoice Count,Sales,COGS,Gross Profit,Margin %,Sales Share %,Outstanding");
            foreach (var r in Rows)
            {
                sb.AppendLine(
                    $"{(r.Rank == 0 ? "-" : r.Rank.ToString())}," +
                    $"{Csv(r.StoreName)},{Csv(r.Location)},{r.InvoiceCount},{r.Sales:F2},{r.COGS:F2},{r.GrossProfit:F2},{r.MarginPercent},{r.SalesSharePercent},{r.Outstanding:F2}");
            }

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
