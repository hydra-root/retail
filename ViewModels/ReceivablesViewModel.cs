using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-05: Customer receivables with ageing buckets (0-30 / 31-60 / 61-90 / 90+) + CSV export.
/// </summary>
public partial class ReceivablesViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<Receivable> _receivables = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private decimal _totalOutstanding;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ReceivablesViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var list = await _reportService.GetReceivablesAsync();
        Receivables = new ObservableCollection<Receivable>(list);
        TotalOutstanding = Receivables.Sum(r => r.OutstandingAmount);
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
            var fileName = $"Receivables_{DateTime.Today:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Receivables Report,{DateTime.Today:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine("Customer,Pending Invoices,Total Outstanding,Current (0-30),31-60,61-90,90+,Oldest Invoice");
            foreach (var r in Receivables)
                sb.AppendLine($"{r.CustomerName},{r.PendingInvoices},{r.OutstandingAmount:F2},{r.Current:F2},{r.Days31To60:F2},{r.Days61To90:F2},{r.Days90Plus:F2},{r.OldestInvoiceDate:dd-MMM-yyyy}");
            sb.AppendLine($"TOTAL,,{TotalOutstanding:F2},{Receivables.Sum(x => x.Current):F2},{Receivables.Sum(x => x.Days31To60):F2},{Receivables.Sum(x => x.Days61To90):F2},{Receivables.Sum(x => x.Days90Plus):F2},");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    public IEnumerable<Receivable> FilteredReceivables =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Receivables
            : Receivables.Where(r => r.CustomerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
}
