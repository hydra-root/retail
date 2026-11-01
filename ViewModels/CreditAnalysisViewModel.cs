using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-11: Credit analysis - limit usage, overdue exposure, days-to-pay per customer.
/// </summary>
public partial class CreditAnalysisViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<CreditAnalysisRow> _rows = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private decimal _totalUsed;

    [ObservableProperty]
    private decimal _totalOverdue;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public CreditAnalysisViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var list = await _reportService.GetCreditAnalysisAsync();
        Rows = new ObservableCollection<CreditAnalysisRow>(list);
        TotalUsed = Rows.Sum(r => r.Used);
        TotalOverdue = Rows.Sum(r => r.OverdueAmount);
        OnPropertyChanged(nameof(CustomersAtRisk));
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
            var fileName = $"CreditAnalysis_{DateTime.Today:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Credit Analysis Report,{DateTime.Today:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine("Customer,Mobile,Credit Limit,Used,Available,Utilization %,Pending Invoices,Overdue,Avg Days To Pay,Risk");
            foreach (var r in FilteredRows)
                sb.AppendLine($"{r.CustomerName},{r.Mobile},{r.CreditLimit:F2},{r.Used:F2},{r.Available:F2},{r.UtilizationPct:F1},{r.PendingInvoices},{r.OverdueAmount:F2},{r.AvgDaysToPay},{r.RiskFlag}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    public int CustomersAtRisk =>
        Rows.Count(r => r.RiskFlag is "Over Limit" or "High" or "Overdue");

    public IEnumerable<CreditAnalysisRow> FilteredRows =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Rows
            : Rows.Where(r => r.CustomerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
}
