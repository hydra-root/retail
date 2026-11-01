using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-02: credit-sales ledger. Toolbar (date range / ageing bucket / customer search /
/// overdue-only), KPI cards, the per-invoice credit grid with a FIFO "settle oldest-first"
/// action for the selected customer, a read-only customer-limit panel, and CSV export.
/// Limit editing is intentionally omitted — that belongs to the Customer master (PAY-04).
/// </summary>
public partial class CreditSalesViewModel : BaseViewModel
{
    private readonly ICreditSaleService _creditSaleService;

    private List<CreditSaleRow> _allRows = new();
    private List<CreditSaleRow> _filteredRows = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerName))]
    [NotifyPropertyChangedFor(nameof(CanSettle))]
    [NotifyPropertyChangedFor(nameof(SelectedSummary))]
    private CreditSaleRow _selectedRow = null!;

    [ObservableProperty]
    private ObservableCollection<CreditSaleRow> _rows = new();

    [ObservableProperty]
    private ObservableCollection<CustomerCreditSummary> _summaries = new();

    [ObservableProperty]
    private ObservableCollection<CreditAllocationPreview> _settlePreview = new();

    [ObservableProperty]
    private DateTime _startDate = FinancialYearStart();

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _bucketFilter = "All buckets";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _overdueOnly;

    [ObservableProperty]
    private decimal _settleAmount;

    [ObservableProperty]
    private string _settlePaymentMode = "Cash";

    [ObservableProperty]
    private string _policySummary = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // KPI cards
    [ObservableProperty]
    private decimal _totalCreditSales;

    [ObservableProperty]
    private decimal _totalOutstanding;

    [ObservableProperty]
    private decimal _totalOverdue;

    [ObservableProperty]
    private int _blockedCustomerCount;

    public List<string> BucketFilters { get; } = new()
    {
        "All buckets", "Settled", "Current (0-30)", "31-60", "61-90", "90+"
    };

    public List<string> PaymentModes { get; } = new()
    {
        "Cash", "UPI", "Card", "Cheque", "Bank Transfer", "NEFT/IMPS"
    };

    public string SelectedCustomerName => SelectedRow == null
        ? "Select an invoice row"
        : $"{SelectedRow.CustomerName}";

    public bool CanSettle => SelectedRow != null && SelectedRow.CustomerId.HasValue;

    /// <summary>Limit panel data for the selected row's customer (read-only).</summary>
    public CustomerCreditSummary? SelectedSummary
    {
        get
        {
            var id = SelectedRow?.CustomerId;
            if (id == null) return null;
            foreach (var s in Summaries)
                if (s.CustomerId == id.Value) return s;
            return null;
        }
    }

    public CreditSalesViewModel(ICreditSaleService creditSaleService)
    {
        _creditSaleService = creditSaleService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // Load all history once; the date/bucket/search/overdue filters run client-side.
            _allRows = await _creditSaleService.GetCreditSalesAsync(
                DateTime.Today.AddYears(-30), DateTime.Today.AddYears(1));
            var summaries = await _creditSaleService.GetCustomerCreditSummaryAsync();
            Summaries = new ObservableCollection<CustomerCreditSummary>(summaries);

            var setting = await _creditSaleService.GetSettingsAsync();
            PolicySummary = setting.PolicySummary;

            RebuildRows();
            ErrorMessage = string.Empty;
        }
        catch (System.Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildRows()
    {
        var from = StartDate.Date;
        var to = EndDate.Date;

        _filteredRows = _allRows
            .Where(r => r.InvoiceDate.Date >= from && r.InvoiceDate.Date <= to)
            .Where(r => !OverdueOnly || r.IsOverdue)
            .Where(r => BucketFilter == "All buckets" || r.Bucket == BucketFilter)
            .Where(MatchesSearch)
            .OrderBy(r => r.DueDate)
            .ThenBy(r => r.InvoiceNumber)
            .ToList();

        Rows = new ObservableCollection<CreditSaleRow>(_filteredRows);

        TotalCreditSales = _filteredRows.Sum(r => r.InvoiceValue);
        TotalOutstanding = _filteredRows.Sum(r => r.Balance);
        TotalOverdue = _filteredRows.Where(r => r.IsOverdue).Sum(r => r.Balance);
        BlockedCustomerCount = Summaries.Count(s => s.IsBlocked);
    }

    private bool MatchesSearch(CreditSaleRow r)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var needle = SearchText.Trim();
        return r.CustomerName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || r.InvoiceNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || r.Gstin.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();
    partial void OnBucketFilterChanged(string value) => RebuildRows();
    partial void OnOverdueOnlyChanged(bool value) => RebuildRows();
    partial void OnStartDateChanged(DateTime value) => RebuildRows();
    partial void OnEndDateChanged(DateTime value) => RebuildRows();

    partial void OnSelectedRowChanged(CreditSaleRow value)
    {
        // Only reset the panel when a row is actually chosen; when Rows rebuilds and selection
        // clears (e.g. right after a settle + reload) we keep the preview and the feedback message.
        if (value == null) return;

        SettlePreview.Clear();
        // Prefill the amount with the customer's total outstanding so "settle" clears them oldest-first.
        var summary = SelectedSummary;
        SettleAmount = summary?.Outstanding ?? value.Balance;
        SuccessMessage = ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    /// <summary>Row-scoped button: focus a customer for settlement (bound via DataContext in XAML).</summary>
    [RelayCommand]
    private void SelectCustomerForRow(CreditSaleRow row)
    {
        if (row == null) return;
        SelectedRow = row;
    }

    [RelayCommand]
    private async Task PreviewSettleAsync()
    {
        SuccessMessage = ErrorMessage = string.Empty;
        var id = SelectedRow?.CustomerId;
        if (id == null) { ErrorMessage = "Select a credit invoice first."; return; }
        if (SettleAmount <= 0) { ErrorMessage = "Enter a settlement amount greater than zero."; return; }

        var lines = await _creditSaleService.PreviewOldestFirstAsync(id.Value, SettleAmount);
        SettlePreview = new ObservableCollection<CreditAllocationPreview>(lines);

        if (lines.Count == 0)
        {
            ErrorMessage = _creditSaleService.LastValidationError ?? "No open invoices to allocate.";
            return;
        }

        var applied = lines.Where(l => !l.IsAdvance).Sum(l => l.Applied);
        var advance = lines.Where(l => l.IsAdvance).Sum(l => l.Applied);
        SuccessMessage = $"FIFO plan: {applied:C} across {lines.Count(l => !l.IsAdvance)} invoice(s)"
                       + (advance > 0 ? $", {advance:C} advance." : ".");
    }

    [RelayCommand]
    private async Task SettleAsync()
    {
        SuccessMessage = ErrorMessage = string.Empty;
        var id = SelectedRow?.CustomerId;
        if (id == null) { ErrorMessage = "Select a credit invoice first."; return; }
        if (SettleAmount <= 0) { ErrorMessage = "Enter a settlement amount greater than zero."; return; }

        var result = await _creditSaleService.SettleOldestFirstAsync(id.Value, SettleAmount, SettlePaymentMode);
        if (!result.Success)
        {
            ErrorMessage = result.Error ?? "Settlement failed.";
            return;
        }

        SettlePreview = new ObservableCollection<CreditAllocationPreview>(result.Lines);
        SuccessMessage = result.Summary;
        SettleAmount = 0;
        await LoadDataAsync();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = $"CreditSales_{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            var csv = _creditSaleService.BuildCsv(_filteredRows, Summaries.ToList(), StartDate, EndDate);

            // UTF-8 BOM so Excel opens the rupee / comma file cleanly (same convention as the sales register).
            File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
            SuccessMessage = $"Exported {_filteredRows.Count} credit sale(s) to {path}";
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    private static DateTime FinancialYearStart() =>
        DateTime.Today.Month >= 4 ? new DateTime(DateTime.Today.Year, 4, 1) : new DateTime(DateTime.Today.Year - 1, 4, 1);
}
