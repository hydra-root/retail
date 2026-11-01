using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-05: Balance sheet as of a date from the chart of accounts, with CSV export.
/// </summary>
public partial class BalanceSheetViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private DateTime _asOfDate = DateTime.Today;

    [ObservableProperty]
    private BalanceSheetReport _report = new();

    [ObservableProperty]
    private ObservableCollection<AccountBalance> _assets = new();

    [ObservableProperty]
    private ObservableCollection<AccountBalance> _liabilities = new();

    [ObservableProperty]
    private ObservableCollection<AccountBalance> _equity = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public string BalancedText =>
        Report.Difference == 0 ? "Balanced ✓" : $"Out of balance by {Report.Difference:C}";

    public BalanceSheetViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        Report = await _reportService.GetBalanceSheetAsync(AsOfDate);
        Assets = new ObservableCollection<AccountBalance>(Report.Assets);
        Liabilities = new ObservableCollection<AccountBalance>(Report.Liabilities);
        Equity = new ObservableCollection<AccountBalance>(Report.Equity);
        OnPropertyChanged(nameof(BalancedText));
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
            var fileName = $"BalanceSheet_{AsOfDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Balance Sheet,As of {AsOfDate:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine("ASSETS");
            sb.AppendLine("Account,Debit,Credit,Balance");
            foreach (var a in Assets)
                sb.AppendLine($"{a.AccountName},{a.DebitTotal:F2},{a.CreditTotal:F2},{a.Balance:F2}");
            sb.AppendLine($"Total Assets,,,{Report.TotalAssets:F2}");
            sb.AppendLine();
            sb.AppendLine("LIABILITIES");
            foreach (var a in Liabilities)
                sb.AppendLine($"{a.AccountName},{a.DebitTotal:F2},{a.CreditTotal:F2},{a.Balance:F2}");
            sb.AppendLine($"Total Liabilities,,,{Report.TotalLiabilities:F2}");
            sb.AppendLine();
            sb.AppendLine("EQUITY");
            foreach (var a in Equity)
                sb.AppendLine($"{a.AccountName},{a.DebitTotal:F2},{a.CreditTotal:F2},{a.Balance:F2}");
            sb.AppendLine($"Retained Earnings (net income),,,{Report.RetainedEarnings:F2}");
            sb.AppendLine($"Total Equity,,,{Report.TotalEquityAccounts + Report.RetainedEarnings:F2}");
            sb.AppendLine();
            sb.AppendLine($"Liabilities + Equity,,,{Report.TotalLiabilitiesAndEquity:F2}");
            sb.AppendLine($"Balanced by,,,{Report.Difference:F2}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }
}
