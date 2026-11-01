using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class ReportsViewModel : BaseViewModel
{
    private readonly IReportService _reportService;
    private readonly IReportExportService _exportService;
    private readonly IScheduledReportService _scheduledReports;

    [ObservableProperty]
    private DashboardStats _stats = new();

    [ObservableProperty]
    private ObservableCollection<DailySales> _dailySales = new();

    [ObservableProperty]
    private ProfitLossReport _profitLoss = new();

    [ObservableProperty]
    private ObservableCollection<StockAudit> _stockAudit = new();

    [ObservableProperty]
    private ObservableCollection<Receivable> _receivables = new();

    [ObservableProperty]
    private GstReport _gstReport = new();

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>RPT-13: result line under the dashboard export buttons.</summary>
    [ObservableProperty]
    private string _exportMessage = string.Empty;

    // ---- RPT-13 scheduled email editor ----

    [ObservableProperty]
    private bool _scheduleEnabled;

    [ObservableProperty]
    private ScheduledReportSetting.ReportKeyChoice? _scheduledReportChoice;

    [ObservableProperty]
    private string _scheduledRecipient = string.Empty;

    [ObservableProperty]
    private string _scheduledSubject = "Daily sales report";

    [ObservableProperty]
    private int _scheduledHour = 8;

    [ObservableProperty]
    private int _scheduledMinute;

    [ObservableProperty]
    private string _scheduledDay = ScheduledReportSetting.DayOfWeekDailyLabel;

    [ObservableProperty]
    private string _scheduleStatusText = string.Empty;

    [ObservableProperty]
    private string _scheduleMessage = string.Empty;

    public List<ScheduledReportSetting.ReportKeyChoice> ReportChoices { get; } =
        ScheduledReportSetting.ReportKeyChoices;

    public List<string> DayChoices { get; } = ScheduledReportSetting.DayOfWeekOptions;

    public IReadOnlyList<int> HourChoices { get; } = Enumerable.Range(0, 24).ToList();

    public IReadOnlyList<int> MinuteChoices { get; } = Enumerable.Range(0, 60).ToList();

    public ReportsViewModel(IReportService reportService, IReportExportService exportService,
        IScheduledReportService scheduledReports)
    {
        _reportService = reportService;
        _exportService = exportService;
        _scheduledReports = scheduledReports;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;

        Stats = await _reportService.GetDashboardStatsAsync();

        var daily = await _reportService.GetDailySalesAsync(StartDate, EndDate);
        DailySales = new ObservableCollection<DailySales>(daily);

        var pl = await _reportService.GetProfitLossReportAsync(StartDate, EndDate);
        ProfitLoss = pl;

        var stock = await _reportService.GetStockAuditAsync();
        StockAudit = new ObservableCollection<StockAudit>(stock);

        var rec = await _reportService.GetReceivablesAsync();
        Receivables = new ObservableCollection<Receivable>(rec);

        var gst = await _reportService.GetGstReportAsync(StartDate, EndDate);
        GstReport = gst;

        IsLoading = false;

        // RPT-13: the scheduled-row pain is deliberately not the data load's problem:
        // if the schedule row cannot be read the reports still come up.
        try
        {
            await LoadScheduleAsync();
        }
        catch (Exception ex)
        {
            ScheduleStatusText = $"Schedule unavailable: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task LoadScheduleAsync()
    {
        var s = await _scheduledReports.GetSettingsAsync();

        ScheduleEnabled = s.IsEnabled;
        ScheduledReportChoice = ReportChoices.FirstOrDefault(c => c.Key == s.ReportKey)
            ?? ReportChoices[0];
        ScheduledRecipient = s.RecipientEmail;
        ScheduledSubject = string.IsNullOrWhiteSpace(s.Subject) ? "Daily sales report" : s.Subject;
        ScheduledHour = Math.Clamp(s.RunAtHour, 0, 23);
        ScheduledMinute = Math.Clamp(s.RunAtMinute, 0, 59);
        ScheduledDay = ScheduledReportSetting.DescribeDay(s.DayOfWeek);

        RefreshScheduleStatus();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    // ---------------------------------------------------------------- exports (RPT-13)

    /// <summary>
    /// RPT-13: export one dashboard section (Stats / Receivables / StockAudit) from the
    /// rows the screen is showing, as PDF or Excel to %TEMP%\RetailBillingReports.
    /// </summary>
    [RelayCommand]
    private async Task ExportPdfAsync(string? section)
    {
        await ExportAsync(section, asPdf: true);
    }

    [RelayCommand]
    private async Task ExportExcelAsync(string? section)
    {
        await ExportAsync(section, asPdf: false);
    }

    private async System.Threading.Tasks.Task ExportAsync(string? section, bool asPdf)
    {
        try
        {
            var sheet = BuildSectionSheet(section);
            var path = asPdf
                ? await _exportService.ExportPdfAsync(sheet)
                : await _exportService.ExportExcelAsync(sheet);

            ExportMessage = $"{(asPdf ? "PDF" : "Excel")} exported: {path}";
        }
        catch (Exception ex)
        {
            ExportMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Shared by the PDF and Excel buttons: maps a section name to its sheet, built
    /// from the data already loaded on this screen (no extra database round-trips).
    /// </summary>
    private ReportExportSheet BuildSectionSheet(string? section) => section switch
    {
        "Receivables" => BuildReceivablesSheet(),
        "StockAudit" => BuildStockAuditSheet(),
        _ => BuildSummarySheet(),
    };

    private ReportExportSheet BuildSummarySheet()
    {
        var sheet = new ReportExportSheet
        {
            Title = "Reports Summary",
            GeneratedAt = DateTime.Now,
            Headers = new() { "Metric", "Value" },
        };

        sheet.Rows.Add(new ReportExportRow(new object?[] { "Cash in Hand", Stats.CashInHand.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "Stock Value", Stats.StockValue.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "Daily Sales", Stats.DailySales.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "Pending Dues", Stats.PendingDues.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "Low Stock Items", Stats.LowStockCount.ToString() }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "Expired Items", Stats.ExpiredCount.ToString() }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "GST Total Sales", GstReport.TotalSales.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "GST Total Tax", GstReport.TotalTax.ToString("N2") }));
        sheet.Rows.Add(new ReportExportRow(new object?[] { "GST Invoices", GstReport.InvoiceCount.ToString() }));
        return sheet;
    }

    private ReportExportSheet BuildReceivablesSheet()
    {
        var sheet = new ReportExportSheet
        {
            Title = "Receivables",
            GeneratedAt = DateTime.Now,
            Headers = new() { "Customer", "Outstanding", "Pending Invoices" },
        };

        sheet.Rows.AddRange(Receivables.Select(r => new ReportExportRow(new object?[]
        {
            r.CustomerName, r.OutstandingAmount.ToString("N2"), r.PendingInvoices.ToString(),
        })));
        sheet.Rows.Add(new ReportExportRow(new object?[]
        {
            "Total",
            Receivables.Sum(r => r.OutstandingAmount).ToString("N2"),
            Receivables.Sum(r => r.PendingInvoices).ToString(),
        }, isTotal: true));
        return sheet;
    }

    private ReportExportSheet BuildStockAuditSheet()
    {
        var sheet = new ReportExportSheet
        {
            Title = "Stock Audit",
            GeneratedAt = DateTime.Now,
            Headers = new() { "Product", "Stock", "Min Qty", "Batch", "Expiry" },
        };

        sheet.Rows.AddRange(StockAudit.Select(r => new ReportExportRow(new object?[]
        {
            r.ProductName,
            r.StockQty.ToString(),
            r.MinStockQty.ToString(),
            r.Batch ?? string.Empty,
            r.ExpiryDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        })));
        return sheet;
    }

    // -------------------------------------------------- scheduled email (RPT-13)

    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        ScheduleMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ScheduledRecipient))
        {
            ScheduleMessage = "Enter a recipient email address first.";
            return;
        }

        var setting = new ScheduledReportSetting
        {
            Id = 1,
            IsEnabled = ScheduleEnabled,
            ReportKey = ScheduledReportChoice?.Key ?? string.Empty,
            RecipientEmail = ScheduledRecipient.Trim(),
            Subject = ScheduledSubject,
            RunAtHour = ScheduledHour,
            RunAtMinute = ScheduledMinute,
            DayOfWeek = ScheduledReportSetting.ToStoredDay(ScheduledDay),
        };

        var (success, error) = await _scheduledReports.SaveSettingsAsync(setting);
        if (success)
        {
            ScheduleMessage = "Schedule saved.";
            // Status properties are cached inside the service; reload from it.
            await LoadScheduleAsync();
        }
        else
        {
            ScheduleMessage = error ?? "Could not save the schedule.";
        }
    }

    [RelayCommand]
    private async Task RunScheduleNowAsync()
    {
        ScheduleMessage = string.Empty;

        var (success, message, _) = await _scheduledReports.RunNowAsync();
        ScheduleMessage = message;
        if (success)
        {
            await LoadScheduleAsync();
        }
    }

    private void RefreshScheduleStatus()
    {
        var last = _scheduledReports.LastRunAt?.ToString("dd-MMM-yyyy HH:mm") ?? "never";
        var next = _scheduledReports.NextRunAt?.ToString("ddd, dd-MMM-yyyy HH:mm") ?? "-";
        var result = string.IsNullOrWhiteSpace(_scheduledReports.LastResult)
            ? "-" : _scheduledReports.LastResult;

        ScheduleStatusText = $"Last run: {last}   |   Next run: {next}   |   Result: {result}";
    }
}
