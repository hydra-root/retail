using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-03: full GSTR-1 (B2B / B2C / CDNR / HSN) + GSTR-3B (outward supplies, ITC, net payable)
/// with JSON export for both returns.
/// </summary>
public partial class GstReportViewModel : BaseViewModel
{
    private readonly IReportService _reportService;

    [ObservableProperty]
    private ObservableCollection<GstB2bInvoice> _b2bInvoices = new();

    [ObservableProperty]
    private ObservableCollection<GstB2cInvoice> _b2cInvoices = new();

    [ObservableProperty]
    private ObservableCollection<GstCreditNote> _creditNotes = new();

    [ObservableProperty]
    private ObservableCollection<GstHsnSummary> _hsnSummary = new();

    /// <summary>Full report payload, used for GSTR-3B summary cards.</summary>
    [ObservableProperty]
    private GstReport _gstr3b = new();

    /// <summary>B2C total (B2C section header total).</summary>
    public decimal TotalB2c => B2cInvoices.Sum(i => i.InvoiceValue);

    /// <summary>CDNR total (credit notes section header total).</summary>
    public decimal TotalCdnr => CreditNotes.Sum(c => c.CreditNoteValue);

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public GstReportViewModel(IReportService reportService)
    {
        _reportService = reportService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var gstReport = await _reportService.GetGstReportAsync(_startDate, _endDate);

            Gstr3b = gstReport;
            B2bInvoices = new ObservableCollection<GstB2bInvoice>(gstReport.B2b);
            B2cInvoices = new ObservableCollection<GstB2cInvoice>(gstReport.B2c);
            CreditNotes = new ObservableCollection<GstCreditNote>(gstReport.CreditNotes);
            HsnSummary = new ObservableCollection<GstHsnSummary>(gstReport.HsnSummary);

            OnPropertyChanged(nameof(TotalB2c));
            OnPropertyChanged(nameof(TotalCdnr));
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ExportGstr1JsonAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var json = await _reportService.ExportGstr1JsonAsync(_startDate, _endDate);
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"GSTR1_{_startDate:yyyyMM}_{_endDate:yyyyMM}.json");
            SuccessMessage = $"GSTR-1 JSON exported ({json.Length:N0} bytes) to {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportGstr3bJsonAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var json = await _reportService.ExportGstr3bJsonAsync(_startDate, _endDate);
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"GSTR3B_{_startDate:yyyyMM}_{_endDate:yyyyMM}.json");
            SuccessMessage = $"GSTR-3B JSON exported ({json.Length:N0} bytes) to {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }
}

public class GstSummary
{
    public decimal TotalSales { get; set; }
    public decimal TotalTax { get; set; }
    public int InvoiceCount { get; set; }
}
