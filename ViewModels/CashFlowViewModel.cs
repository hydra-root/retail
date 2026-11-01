using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-06: Cash flow statement (Cash + Bank journal movements) with CSV export.
/// </summary>
public partial class CashFlowViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private CashFlowReport _report = new();

    [ObservableProperty]
    private ObservableCollection<CashFlowRow> _rows = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public CashFlowViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        Report = await _reportService.GetCashFlowAsync(StartDate, EndDate);
        Rows = new ObservableCollection<CashFlowRow>(Report.Rows);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = $"CashFlow_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Cash Flow Statement,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine($"Opening Balance,{Report.OpeningBalance:F2}");
            sb.AppendLine($"Net Cash Flow,{Report.NetCashFlow:F2}");
            sb.AppendLine($"Closing Balance,{Report.ClosingBalance:F2}");
            sb.AppendLine();
            sb.AppendLine("Source,Inflow,Outflow,Net");
            foreach (var r in Rows)
                sb.AppendLine($"{r.Source},{r.Inflow:F2},{r.Outflow:F2},{r.Net:F2}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }
}
