using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-09: Supplier outstanding with ageing buckets (0-30 / 31-60 / 61-90 / 90+) + CSV export.
/// </summary>
public partial class PayablesViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<Payable> _payables = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private decimal _totalOutstanding;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public PayablesViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var list = await _reportService.GetPayablesAsync();
        Payables = new ObservableCollection<Payable>(list);
        TotalOutstanding = Payables.Sum(p => p.Amount);
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
            var fileName = $"Payables_{DateTime.Today:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"Payables Report,{DateTime.Today:dd-MMM-yyyy}");
            sb.AppendLine();
            sb.AppendLine("Supplier,Pending Bills,Total Outstanding,Current (0-30),31-60,61-90,90+,Earliest Bill Due");
            foreach (var p in Payables)
                sb.AppendLine($"{p.SupplierName},{p.PendingBills},{p.Amount:F2},{p.Current:F2},{p.Days31To60:F2},{p.Days61To90:F2},{p.Days90Plus:F2},{p.DueDate:dd-MMM-yyyy}");
            sb.AppendLine($"TOTAL,,{TotalOutstanding:F2},{Payables.Sum(p => p.Current):F2},{Payables.Sum(p => p.Days31To60):F2},{Payables.Sum(p => p.Days61To90):F2},{Payables.Sum(p => p.Days90Plus):F2},");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Report exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    public IEnumerable<Payable> FilteredPayables =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Payables
            : Payables.Where(p => p.SupplierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
}
