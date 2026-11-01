using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// RPT-02 / PAY-13: the accountant's sales register - one row per invoice, a day-total row
/// after each trading day, and period totals. Estimates are kept out of the totals because
/// they are not sales yet, but stay visible so nothing looks lost.
/// </summary>
public partial class SalesRegisterViewModel : BaseViewModel
{
    private const decimal Epsilon = 0.01m;

    private readonly IInvoiceService _invoiceService;

    [ObservableProperty]
    private ObservableCollection<SalesRegisterRow> _rows = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaymentModeFilters))]
    private List<Invoice> _allInvoices = new();

    [ObservableProperty]
    private DateTime _startDate = FinancialYearStart();

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _typeFilter = "All types";

    [ObservableProperty]
    private string _paymentModeFilter = "All modes";

    [ObservableProperty]
    private bool _duesOnly;

    [ObservableProperty]
    private bool _showDayTotals = true;

    [ObservableProperty]
    private bool _includeEstimates;

    [ObservableProperty]
    private SalesRegisterTotals _totals = new();

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> TypeFilters { get; } = new() { "All types", "GST Invoice", "Bill of Supply", "Estimate" };

    public SalesRegisterViewModel(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            AllInvoices = await _invoiceService.GetAllInvoicesAsync();
            RebuildRows();
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

    private void RebuildRows()
    {
        var inRange = AllInvoices
            .Where(i => i.InvoiceDate.Date >= StartDate.Date && i.InvoiceDate.Date <= EndDate.Date)
            .Where(MatchesFilter)
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNumber)
            .ToList();

        var rows = new List<SalesRegisterRow>();
        var dayGroups = inRange.GroupBy(i => i.InvoiceDate.Date).OrderBy(g => g.Key);

        foreach (var day in dayGroups)
        {
            foreach (var invoice in day)
                rows.Add(SalesRegisterRow.FromInvoice(invoice));

            if (!ShowDayTotals) continue;

            // Day totals follow the printed register convention and exclude estimates,
            // which are quotations rather than completed sales.
            var real = day.Where(i => !i.IsEstimate).ToList();
            if (real.Count == 0) continue;

            rows.Add(new SalesRegisterRow
            {
                IsSummary = true,
                InvoiceNumber = $"Day total - {day.Key:dd-MMM-yyyy}",
                Date = day.Key,
                CustomerName = $"{real.Count} invoice(s)",
                Taxable = real.Sum(i => i.SubTotal),
                CGST = real.Sum(i => i.CGST),
                SGST = real.Sum(i => i.SGST),
                IGST = real.Sum(i => i.IGST),
                Cess = real.Sum(i => i.Cess),
                Total = real.Sum(i => i.TotalAmount),
                Paid = real.Sum(i => i.PaidAmount),
                Balance = real.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount),
            });
        }

        var periodReal = inRange.Where(i => !i.IsEstimate).ToList();
        rows.Add(new SalesRegisterRow
        {
            IsSummary = true,
            InvoiceNumber = $"PERIOD TOTAL - {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
            Date = EndDate.Date,
            CustomerName = $"{periodReal.Count} invoice(s)"
                           + (inRange.Count(i => i.IsEstimate) > 0 ? $", {inRange.Count(i => i.IsEstimate)} estimate(s)" : string.Empty),
            Taxable = periodReal.Sum(i => i.SubTotal),
            CGST = periodReal.Sum(i => i.CGST),
            SGST = periodReal.Sum(i => i.SGST),
            IGST = periodReal.Sum(i => i.IGST),
            Cess = periodReal.Sum(i => i.Cess),
            Total = periodReal.Sum(i => i.TotalAmount),
            Paid = periodReal.Sum(i => i.PaidAmount),
            Balance = periodReal.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount),
        });

        Rows = new ObservableCollection<SalesRegisterRow>(rows);
        Totals = SalesRegisterTotals.From(periodReal);
    }

    private bool MatchesFilter(Invoice i)
    {
        if (!IncludeEstimates && i.IsEstimate) return false;

        if (TypeFilter != "All types" && i.TypeLabel != TypeFilter) return false;

        if (PaymentModeFilter != "All modes" &&
            !string.Equals(i.PaymentMode, PaymentModeFilter, StringComparison.OrdinalIgnoreCase)) return false;

        if (DuesOnly && i.TotalAmount - i.PaidAmount - i.WriteOffAmount <= Epsilon) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var needle = SearchText.Trim();
        return i.InvoiceNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (i.CustomerName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || (i.CustomerGSTIN?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();
    partial void OnTypeFilterChanged(string value) => RebuildRows();
    partial void OnPaymentModeFilterChanged(string value) => RebuildRows();
    partial void OnDuesOnlyChanged(bool value) => RebuildRows();
    partial void OnShowDayTotalsChanged(bool value) => RebuildRows();
    partial void OnIncludeEstimatesChanged(bool value) => RebuildRows();
    partial void OnStartDateChanged(DateTime value) => RebuildRows();
    partial void OnEndDateChanged(DateTime value) => RebuildRows();

    public List<string> PaymentModeFilters =>
        AllInvoices.Select(i => i.PaymentMode)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .OrderBy(m => m)
            .Prepend("All modes")
            .ToList();

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private void SetCurrentMonth()
    {
        StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        EndDate = DateTime.Today;
    }

    [RelayCommand]
    private void SetCurrentFinancialYear()
    {
        StartDate = FinancialYearStart();
        EndDate = DateTime.Today;
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = $"SalesRegister_{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Sales Register,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine($"Invoices,{Totals.InvoiceCount},Gross sales,{Totals.GrossSales:F2}");
            sb.AppendLine($"Taxable,{Totals.Taxable:F2},Tax collected,{Totals.TaxCollected:F2}");
            sb.AppendLine($"Collected,{Totals.Collected:F2},Balance,{Totals.Balance:F2}");
            sb.AppendLine();
            sb.AppendLine("Invoice,Date,Customer,GSTIN,Place of supply,Type,Items,Taxable,CGST,SGST,IGST,Cess,Total,Paid,Balance,Mode,Status");
            foreach (var r in Rows)
            {
                sb.AppendLine(string.Join(',',
                    Escape(r.InvoiceNumber), r.Date.ToString("yyyy-MM-dd"), Escape(r.CustomerName),
                    Escape(r.Gstin), Escape(r.PlaceOfSupply), Escape(r.Type),
                    r.IsSummary ? string.Empty : r.Items.ToString(),
                    r.Taxable.ToString("F2"), r.CGST.ToString("F2"), r.SGST.ToString("F2"),
                    r.IGST.ToString("F2"), r.Cess.ToString("F2"), r.Total.ToString("F2"),
                    r.Paid.ToString("F2"), r.Balance.ToString("F2"), Escape(r.PaymentMode), Escape(r.Status)));
            }

            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
            SuccessMessage = $"Exported {Rows.Count(r => !r.IsSummary)} invoice(s) to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static DateTime FinancialYearStart() =>
        DateTime.Today.Month >= 4 ? new DateTime(DateTime.Today.Year, 4, 1) : new DateTime(DateTime.Today.Year - 1, 4, 1);
}

public class SalesRegisterRow
{
    public bool IsSummary { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Gstin { get; set; } = string.Empty;
    public string PlaceOfSupply { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Items { get; set; }
    public decimal Taxable { get; set; }
    public decimal CGST { get; set; }
    public decimal SGST { get; set; }
    public decimal IGST { get; set; }
    public decimal Cess { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public static SalesRegisterRow FromInvoice(Invoice i) => new()
    {
        InvoiceNumber = i.InvoiceNumber,
        Date = i.InvoiceDate,
        CustomerName = string.IsNullOrWhiteSpace(i.CustomerName) ? "Cash sale" : i.CustomerName,
        Gstin = string.IsNullOrWhiteSpace(i.CustomerGSTIN) ? "Unregistered" : i.CustomerGSTIN,
        PlaceOfSupply = i.PlaceOfSupply ?? string.Empty,
        Type = i.TypeLabel,
        Items = i.Items.Count,
        Taxable = i.SubTotal,
        CGST = i.CGST,
        SGST = i.SGST,
        IGST = i.IGST,
        Cess = i.Cess,
        Total = i.TotalAmount,
        Paid = i.PaidAmount,
        Balance = i.TotalAmount - i.PaidAmount - i.WriteOffAmount,
        PaymentMode = i.PaymentMode,
        Status = i.Status,
    };
}

public class SalesRegisterTotals
{
    public int InvoiceCount { get; set; }
    public int LineItemCount { get; set; }
    public decimal Taxable { get; set; }
    public decimal TaxCollected { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Collected { get; set; }
    public decimal Balance { get; set; }
    public decimal AverageBill { get; set; }
    public int UniqueCustomers { get; set; }
    public int DuesCount { get; set; }

    public static SalesRegisterTotals From(List<Invoice> invoices)
    {
        var taxable = invoices.Sum(i => i.SubTotal);
        var tax = invoices.Sum(i => i.CGST + i.SGST + i.IGST + i.Cess);
        var gross = invoices.Sum(i => i.TotalAmount);

        return new SalesRegisterTotals
        {
            InvoiceCount = invoices.Count,
            LineItemCount = invoices.Sum(i => i.Items.Count),
            Taxable = taxable,
            TaxCollected = tax,
            GrossSales = gross,
            Collected = invoices.Sum(i => i.PaidAmount),
            Balance = invoices.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount),
            AverageBill = invoices.Count > 0 ? gross / invoices.Count : 0m,
            UniqueCustomers = invoices.Where(i => i.CustomerId.HasValue).Select(i => i.CustomerId).Distinct().Count(),
            DuesCount = invoices.Count(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount > 0.01m),
        };
    }
}
