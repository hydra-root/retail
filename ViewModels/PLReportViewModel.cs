using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-04: Profit &amp; Loss report with revenue, COGS, gross profit, expenses,
/// net profit, monthly summaries and expense breakdown + CSV export.
/// </summary>
public partial class PLReportViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private ProfitLossReport _report = new();

    [ObservableProperty]
    private ObservableCollection<MonthlySummary> _monthlySummaries = new();

    [ObservableProperty]
    private ObservableCollection<CategorySummary> _expenseBreakdown = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public PLReportViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        Report = await _reportService.GetProfitLossReportAsync(StartDate, EndDate);
        MonthlySummaries = new ObservableCollection<MonthlySummary>(Report.MonthlySummaries);
        ExpenseBreakdown = new ObservableCollection<CategorySummary>(Report.ExpenseBreakdown);
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
            var fileName = $"PL_Report_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Profit & Loss Report,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine($"Total Revenue (excl. tax),{Report.TotalRevenue:F2}");
            sb.AppendLine($"Total COGS,{Report.TotalCOGS:F2}");
            sb.AppendLine($"Gross Profit,{Report.GrossProfit:F2}");
            sb.AppendLine($"Gross Margin %,{Report.GrossMarginPct:F2}");
            sb.AppendLine($"Total Expenses,{Report.TotalExpenses:F2}");
            sb.AppendLine($"Net Profit,{Report.NetProfit:F2}");
            sb.AppendLine();
            sb.AppendLine("Monthly Summary");
            sb.AppendLine("Month,Revenue,COGS,Expenses,Profit");
            foreach (var m in MonthlySummaries)
                sb.AppendLine($"{m.Month},{m.Revenue:F2},{m.COGS:F2},{m.Expenses:F2},{m.Profit:F2}");
            sb.AppendLine();
            sb.AppendLine("Expense Breakdown");
            sb.AppendLine("Category,Amount");
            foreach (var e in ExpenseBreakdown)
                sb.AppendLine($"{e.Category},{e.Amount:F2}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }
}
