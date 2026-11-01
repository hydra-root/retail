using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Controls;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace RetailBillingApp.ViewModels;

/// <summary>A trend-chart period option for the dashboard selector.</summary>
public sealed record ChartPeriod(int Days, string Label);

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IReportService _reportService;
    private readonly IProductService _productService;
    private readonly ISaleService _saleService;
    private readonly IInvoiceService _invoiceService;

    // Cached so the period selector can re-drive the trend and payment mix without re-querying.
    private List<Product> _allProducts = new();
    private List<Invoice> _allInvoices = new();

    [ObservableProperty]
    private DashboardStats _stats = new();

    [ObservableProperty]
    private ObservableCollection<StockAudit> _lowStockItems = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private IReadOnlyList<ChartPoint>? _salesTrend;

    [ObservableProperty]
    private IReadOnlyList<ChartPoint>? _paymentMix;

    [ObservableProperty]
    private IReadOnlyList<ChartPoint>? _receivablesAgeing;

    [ObservableProperty]
    private IReadOnlyList<ChartPoint>? _stockHealth;

    [ObservableProperty]
    private IReadOnlyList<ChartPoint>? _topStockValues;

    [ObservableProperty]
    private string _salesTrendSummary = string.Empty;

    [ObservableProperty]
    private string _receivablesTotalText = ChartFormat.Money(0);

    [ObservableProperty]
    private string _paymentMixCenterValue = string.Empty;

    [ObservableProperty]
    private string _stockHealthCenterValue = string.Empty;

    [ObservableProperty]
    private ChartPeriod? _selectedPeriod;

    public IReadOnlyList<ChartPeriod> PeriodOptions { get; }

    public DashboardViewModel(IReportService reportService, IProductService productService,
        ISaleService saleService, IInvoiceService invoiceService)
    {
        _reportService = reportService;
        _productService = productService;
        _saleService = saleService;
        _invoiceService = invoiceService;

        PeriodOptions = new[]
        {
            new ChartPeriod(7, "Last 7 days"),
            new ChartPeriod(14, "Last 14 days"),
            new ChartPeriod(30, "Last 30 days"),
        };
        // Assign the backing field so no change handler runs before the first load.
        _selectedPeriod = PeriodOptions[1];
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            Stats = await _reportService.GetDashboardStatsAsync();

            var lowStock = await _productService.GetLowStockProductsAsync();
            LowStockItems = new ObservableCollection<StockAudit>(
                lowStock.Select(p => new StockAudit
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    StockQty = p.StockQty,
                    MinStockQty = p.MinStockQty,
                    Batch = p.Batch,
                    ExpiryDate = p.ExpiryDate
                }));

            _allProducts = await _productService.GetAllProductsAsync();
            _allInvoices = await _invoiceService.GetAllInvoicesAsync();

            await LoadChartsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async System.Threading.Tasks.Task LoadChartsAsync()
    {
        await BuildTrendAsync();
        await BuildPaymentMixAsync();
        await BuildReceivablesAgeingAsync();
        BuildStockHealth();
    }

    partial void OnSelectedPeriodChanged(ChartPeriod? value)
    {
        // Only the two period-based charts re-run; ageing and stock health are "as of today".
        if (_allInvoices.Count == 0 && _allProducts.Count == 0) return;
        _ = ReloadPeriodAsync();
    }

    private async System.Threading.Tasks.Task ReloadPeriodAsync()
    {
        try
        {
            await BuildTrendAsync();
            await BuildPaymentMixAsync();
        }
        catch { /* dashboard must never surface a chart refresh failure */ }
    }

    private (DateTime from, DateTime to, int days) PeriodRange()
    {
        int days = SelectedPeriod?.Days ?? 14;
        var to = System.DateTime.Today;
        return (to.AddDays(-(days - 1)), to, days);
    }

    /// <summary>Sales trend columns + overlaid invoice-count line, fed by GetDailySalesAsync.</summary>
    private async System.Threading.Tasks.Task BuildTrendAsync()
    {
        var (from, to, days) = PeriodRange();
        var daily = await _reportService.GetDailySalesAsync(from, to.AddDays(1).AddSeconds(-1));

        var byDate = new Dictionary<System.DateTime, DailySales>();
        foreach (var d in daily) byDate[d.Date.Date] = d;

        var points = new List<ChartPoint>(days);
        decimal total = 0m;
        int invoiceTotal = 0;
        for (int i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            byDate.TryGetValue(date, out var sales);
            decimal amount = sales?.Amount ?? 0m;
            int count = sales?.InvoiceCount ?? 0;
            total += amount;
            invoiceTotal += count;

            points.Add(new ChartPoint
            {
                Label = date.ToString("dd/MM"),
                Value = (double)amount,
                DisplayText = ChartFormat.MoneyCompact((double)amount),
                TooltipText = $"{date:dd MMM yyyy} · {ChartFormat.Money(amount)}",
                SecondaryValue = count,
                SecondaryText = $"{count} invoice{(count == 1 ? string.Empty : "s")}"
            });
        }

        SalesTrend = points;
        SalesTrendSummary = $"{ChartFormat.Money(total)} · {invoiceTotal} invoices";
    }

    /// <summary>Donut computed locally from invoices in the period, grouped by PaymentMode (estimates excluded).</summary>
    private async System.Threading.Tasks.Task BuildPaymentMixAsync()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        var (from, to, _) = PeriodRange();

        var byMode = new Dictionary<string, decimal>();
        foreach (var inv in _allInvoices)
        {
            if (inv.IsEstimate) continue;
            var day = inv.InvoiceDate.Date;
            if (day < from || day > to) continue;
            var mode = NormalizeMode(inv.PaymentMode);
            byMode[mode] = byMode.GetValueOrDefault(mode) + inv.TotalAmount;
        }

        var slices = new List<ChartPoint>();
        decimal total = 0m;
        foreach (var canonical in new[] { "Cash", "Card", "UPI", "Credit", "Split" })
        {
            if (byMode.TryGetValue(canonical, out var v) && v > 0)
            {
                slices.Add(ModeSlice(canonical, v));
                total += v;
                byMode.Remove(canonical);
            }
        }
        foreach (var kv in byMode.OrderByDescending(k => k.Value))
        {
            if (kv.Value <= 0) continue;
            slices.Add(ModeSlice(kv.Key, kv.Value));
            total += kv.Value;
        }

        PaymentMix = slices;
        PaymentMixCenterValue = ChartFormat.MoneyCompact((double)total);
    }

    /// <summary>Horizontal ageing bars, aggregating the per-customer Receivable buckets.</summary>
    private async System.Threading.Tasks.Task BuildReceivablesAgeingAsync()
    {
        var receivables = await _reportService.GetReceivablesAsync();

        decimal current = receivables.Sum(r => r.Current);
        decimal d31 = receivables.Sum(r => r.Days31To60);
        decimal d61 = receivables.Sum(r => r.Days61To90);
        decimal d90 = receivables.Sum(r => r.Days90Plus);

        ReceivablesAgeing = new[]
        {
            AgeSlice("0-30 days", current, SuccessBrush),
            AgeSlice("31-60 days", d31, AccentBrush),
            AgeSlice("61-90 days", d61, WarningBrush),
            AgeSlice("90+ days", d90, ErrorBrush),
        };
        ReceivablesTotalText = ChartFormat.Money(current + d31 + d61 + d90);
    }

    /// <summary>Stock-health donut (healthy vs low SKUs) plus the top-5 products by on-hand value.</summary>
    private void BuildStockHealth()
    {
        int total = _allProducts.Count;
        int low = _allProducts.Count(p => p.StockQty <= p.MinStockQty);
        int healthy = System.Math.Max(0, total - low);

        StockHealth = new[]
        {
            CountSlice("Healthy", healthy, SuccessBrush),
            CountSlice("Low stock", low, WarningBrush),
        };
        StockHealthCenterValue = total.ToString("N0");

        TopStockValues = _allProducts
            .Select(p => new { p.Name, Value = p.Rate * p.StockQty })
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .Take(5)
            .Select(x => new ChartPoint
            {
                Label = x.Name,
                Value = (double)x.Value,
                DisplayText = ChartFormat.MoneyCompact((double)x.Value),
                TooltipText = ChartFormat.Money(x.Value)
            })
            .ToList();
    }

    private static ChartPoint ModeSlice(string mode, decimal amount) => new()
    {
        Label = mode,
        Value = (double)amount,
        DisplayText = ChartFormat.MoneyCompact((double)amount),
        TooltipText = ChartFormat.Money(amount),
        Fill = ModeBrush(mode)
    };

    private static ChartPoint AgeSlice(string label, decimal amount, Brush fill) => new()
    {
        Label = label,
        Value = (double)amount,
        DisplayText = ChartFormat.MoneyCompact((double)amount),
        TooltipText = ChartFormat.Money(amount),
        Fill = fill
    };

    private static ChartPoint CountSlice(string label, int count, Brush fill) => new()
    {
        Label = label,
        Value = count,
        DisplayText = count.ToString("N0"),
        TooltipText = $"{count} items",
        Fill = fill
    };

    private static string NormalizeMode(string? mode)
    {
        var s = (mode ?? string.Empty).Trim();
        switch (s.ToLowerInvariant())
        {
            case "cash": return "Cash";
            case "card": return "Card";
            case "upi": return "UPI";
            case "credit": return "Credit";
            case "split": return "Split";
            default:
                if (string.IsNullOrWhiteSpace(s)) return "Other";
                return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        }
    }

    private static Brush? ModeBrush(string canonical) => canonical switch
    {
        "Cash" => CashBrush,
        "Card" => SuccessBrush,
        "UPI" => UpiBrush,
        "Credit" => WarningBrush,
        "Split" => SplitBrush,
        _ => null,
    };

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    // Palette locked to AppStyles so charts match the KPI cards.
    private static readonly Brush CashBrush = Hex("#1976D2");
    private static readonly Brush UpiBrush = Hex("#00BCD4");
    private static readonly Brush SplitBrush = Hex("#9C27B0");
    private static readonly Brush SuccessBrush = Hex("#4CAF50");
    private static readonly Brush WarningBrush = Hex("#FF9800");
    private static readonly Brush ErrorBrush = Hex("#F44336");
    private static readonly Brush AccentBrush = Hex("#FFC107");

    private static Brush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
