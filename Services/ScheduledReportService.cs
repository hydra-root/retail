using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.Services;

/// <summary>
/// RPT-13: emails one of the standard reports as a PDF on a daily/weekly schedule.
/// Construction mirrors BackupSchedulerService: singleton-friendly, no scoped
/// AppDbContext held for its lifetime (a captive dependency shared with the UI
/// thread's context) — every operation opens its own IServiceScopeFactory scope.
/// </summary>
public class ScheduledReportService : IScheduledReportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuthService _authService;

    // Serializes the two DB-writing entry points (the tick and a manual Run Now) so
    // the scheduler's own reads/SaveChanges never interleave with another run.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private System.Threading.Timer? _timer;
    // Non-awaitable guard so a slow run can't let two ticks overlap.
    private int _tickRunning;

    // Cached mirror of the persisted row so the status card can read it without a query.
    private DateTime? _lastRunAt;
    private DateTime? _nextRunAt;
    private string? _lastResult;

    public ScheduledReportService(IServiceScopeFactory scopeFactory, IAuthService authService)
    {
        _scopeFactory = scopeFactory;
        _authService = authService;
    }

    public bool IsRunning => _timer != null;
    public DateTime? LastRunAt => _lastRunAt;
    public DateTime? NextRunAt => _nextRunAt;
    public string? LastResult => _lastResult;

    public async Task<ScheduledReportSetting> GetSettingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = await GetOrCreateSettingsAsync(Db(scope));
        UpdateCache(settings);
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(ScheduledReportSetting setting)
    {
        if (setting == null)
            return (false, "No schedule to save.");

        if (!string.IsNullOrEmpty(setting.ReportKey) &&
            !ScheduledReportSetting.ReportKeyOptions.Contains(setting.ReportKey))
            return (false, $"Unknown report key '{setting.ReportKey}'.");

        var day = ScheduledReportSetting.ToStoredDay(setting.DayOfWeek);
        if (day != ScheduledReportSetting.DayOfWeekDaily &&
            !Array.Exists(System.Enum.GetNames(typeof(System.DayOfWeek)),
                n => n.Equals(day, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Unknown weekday '{day}'.");

        if (string.IsNullOrWhiteSpace(setting.RecipientEmail))
            return (false, "Enter a recipient email address.");

        await _gate.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = Db(scope);
            var current = await GetOrCreateSettingsAsync(db);
            var before = Snapshot(current);

            current.IsEnabled = setting.IsEnabled;
            current.ReportKey = setting.ReportKey?.Trim() ?? string.Empty;
            current.ReportTitle = ScheduledReportSetting.DescribeReportKey(current.ReportKey);
            current.RecipientEmail = setting.RecipientEmail.Trim();
            current.Subject = string.IsNullOrWhiteSpace(setting.Subject)
                ? "Daily sales report" : setting.Subject.Trim();
            current.RunAtHour = Math.Clamp(setting.RunAtHour, 0, 23);
            current.RunAtMinute = Math.Clamp(setting.RunAtMinute, 0, 59);
            current.DayOfWeek = day;

            // Re-anchor the slot to "now" so a freshly (re)saved schedule waits for its
            // next occurrence instead of firing immediately on the timer's next tick.
            current.NextRunAt = ComputeNextRun(current, DateTime.Now);
            current.UpdatedAt = DateTime.UtcNow;
            current.UpdatedBy = _authService.CurrentUser?.Username;

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
                return (false, "Failed to save the scheduled report.");
            }

            UpdateCache(current);
            await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(
                "Update", "ScheduledReport", current.Id, current.ReportTitle,
                before, Snapshot(current));

            return (true, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new System.Threading.Timer(OnTick, null,
            dueTime: TimeSpan.FromSeconds(20),
            period: TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0) return;

        // Timer callbacks run on a pool thread; RunIfDueAsync swallows its own exceptions,
        // but guard here too so nothing can ever surface on the timer thread.
        _ = Task.Run(async () =>
        {
            try { await RunIfDueAsync(); }
            catch { /* defensive */ }
            finally { Interlocked.Exchange(ref _tickRunning, 0); }
        });
    }

    public async Task<bool> RunIfDueAsync()
    {
        try
        {
            var now = DateTime.Now;
            await _gate.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = Db(scope);
                var s = await GetOrCreateSettingsAsync(db);
                UpdateCache(s);
                if (!s.IsEnabled) return false;
                if (string.IsNullOrEmpty(s.ReportKey))
                {
                    // No report chosen yet — stay silent (never spam the audit log with
                    // "no report configured" failures on every slot).
                    s.NextRunAt = ComputeNextRun(s, now);
                    await db.SaveChangesAsync();
                    UpdateCache(s);
                    return false;
                }

                if (s.NextRunAt == null)
                {
                    s.NextRunAt = ComputeNextRun(s, now);
                    await db.SaveChangesAsync();
                    UpdateCache(s);
                    return false;
                }

                if (now < s.NextRunAt) return false;

                var outcome = await PerformReportAsync(scope.ServiceProvider, s, manual: false);

                s.LastRunAt = DateTime.Now;
                s.LastResult = outcome.Message;
                s.UpdatedAt = DateTime.UtcNow;
                s.UpdatedBy = _authService.CurrentUser?.Username;
                // Compute from the just-completed run so missed slots collapse into one.
                s.NextRunAt = ComputeNextRun(s, s.LastRunAt.Value);
                await db.SaveChangesAsync();
                UpdateCache(s);

                await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(
                    "Run", "ScheduledReport", s.Id, s.ReportTitle, null,
                    new { outcome.Success, outcome.Message, Path = outcome.Path });

                return outcome.Success;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            // Never let an exception escape the tick. Record it for the status card; if the
            // context is unusable the in-memory mirror still shows the reason until the next load.
            _lastResult = $"Scheduled report failed: {ex.Message}";
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = Db(scope);
                var s = await GetOrCreateSettingsAsync(db);
                s.LastResult = _lastResult;
                await db.SaveChangesAsync();
            }
            catch { /* ignore secondary persistence failure */ }
            return false;
        }
    }

    public async Task<(bool Success, string Message, string? Path)> RunNowAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = Db(scope);
            var s = await GetOrCreateSettingsAsync(db);
            UpdateCache(s);

            if (string.IsNullOrEmpty(s.ReportKey))
                return (false, "Choose a report first — nothing is scheduled to send.", null);

            var outcome = await PerformReportAsync(scope.ServiceProvider, s, manual: true);

            s.LastRunAt = DateTime.Now;
            s.LastResult = outcome.Message;
            s.UpdatedAt = DateTime.UtcNow;
            s.UpdatedBy = _authService.CurrentUser?.Username;
            s.NextRunAt = ComputeNextRun(s, s.LastRunAt.Value) ?? s.NextRunAt;
            await db.SaveChangesAsync();
            UpdateCache(s);

            await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync(
                "Run", "ScheduledReport", s.Id, s.ReportTitle, null,
                new { outcome.Success, outcome.Message, Path = outcome.Path });

            return (outcome.Success, outcome.Message, outcome.Path);
        }
        catch (Exception ex)
        {
            return (false, $"Scheduled report run failed: {ex.Message}", null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public DateTime? ComputeNextRun(ScheduledReportSetting setting, DateTime from)
    {
        if (setting == null) return null;

        var slot = new TimeSpan(
            Math.Clamp(setting.RunAtHour, 0, 23),
            Math.Clamp(setting.RunAtMinute, 0, 59),
            0);

        var day = ScheduledReportSetting.ToStoredDay(setting.DayOfWeek);
        if (day == ScheduledReportSetting.DayOfWeekDaily)
        {
            var candidate = from.Date + slot;
            if (candidate <= from) candidate = candidate.AddDays(1);
            return candidate;
        }

        // Weekly: next occurrence of the chosen weekday (name must be valid; save validates it).
        if (!System.Enum.TryParse(day, ignoreCase: true, out System.DayOfWeek target))
            target = System.DayOfWeek.Monday;

        var weekly = from.Date + slot;
        var delta = ((int)target - (int)weekly.DayOfWeek + 7) % 7;
        weekly = weekly.AddDays(delta);
        if (weekly <= from) weekly = weekly.AddDays(7);
        return weekly;
    }

    public async Task<(ReportExportSheet? Sheet, string? Error)> BuildSheetForReportAsync(
        ScheduledReportSetting setting, DateTime from)
    {
        if (setting == null)
            return (null, "No schedule configured.");

        using var scope = _scopeFactory.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        return await BuildSheetForReportAsync(reports, setting, from);
    }

    // ---- internals ----

    private record ReportOutcome(bool Success, string Message, string? Path);

    private static AppDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static async Task<ScheduledReportSetting> GetOrCreateSettingsAsync(AppDbContext db)
    {
        var settings = await db.ScheduledReportSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new ScheduledReportSetting { Id = 1 };
            db.ScheduledReportSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    /// <summary>Builds the PDF and sends the email. Assumes the caller holds the gate.</summary>
    private async Task<ReportOutcome> PerformReportAsync(
        IServiceProvider scoped, ScheduledReportSetting s, bool manual)
    {
        if (string.IsNullOrEmpty(s.ReportKey))
            return new ReportOutcome(false, "No report key configured — choose a report first.", null);

        var when = DateTime.Now;
        var reports = scoped.GetRequiredService<IReportService>();
        var exportService = scoped.GetRequiredService<IReportExportService>();

        var (sheet, buildError) = await BuildSheetForReportAsync(reports, s, when);
        if (sheet == null)
            return new ReportOutcome(false, $"Report build failed: {buildError}", null);

        string pdfPath;
        try
        {
            pdfPath = await exportService.ExportPdfAsync(sheet);
        }
        catch (Exception ex)
        {
            return new ReportOutcome(false, $"PDF export failed: {ex.Message}", null);
        }

        // IMessagingService.SendEmailAsync has no attachment support yet, so the PDF
        // file path is put in the body and the recipient opens it from %TEMP%.
        var period = DescribeRange(s.ReportKey);
        var body = new System.Text.StringBuilder()
            .AppendLine(s.Subject)
            .AppendLine()
            .AppendLine($"{s.ReportTitle} - {period}")
            .AppendLine($"Generated: {when:dd-MMM-yyyy HH:mm}")
            .AppendLine($"Rows included: {sheet.Rows.Count}")
            .AppendLine()
            .AppendLine($"PDF file: {pdfPath}")
            .AppendLine()
            .AppendLine("Retail Billing App - scheduled report (RPT-13).")
            .ToString();

        var (sent, status, detail) = await scoped.GetRequiredService<IMessagingService>()
            .SendEmailAsync(s.RecipientEmail, s.Subject, body, "ScheduledReport");

        var message = $"{status}. {detail}";
        if (sent && status == "Opened in browser")
            message = "No SMTP configured - mailto compose window opened; the report PDF is at " + pdfPath;

        return new ReportOutcome(sent, message, pdfPath);
    }

    private async Task<(ReportExportSheet? Sheet, string? Error)> BuildSheetForReportAsync(
        IReportService reports, ScheduledReportSetting s, DateTime when)
    {
        try
        {
            // Sensible fixed ranges, independent of the report screen's pickers:
            // windowed reports cover the last 7 days ending yesterday (a "daily
            // email" should not include today's half-finished day), snapshot
            // reports ignore dates entirely — exactly what their screen shows.
            var yesterday = when.Date.AddDays(-1);
            var from7 = yesterday.AddDays(-6);

            switch (s.ReportKey)
            {
                case ScheduledReportSetting.ReportKeyDailySales:
                {
                    var rows = await reports.GetDailySalesAsync(from7, yesterday);
                    var sheet = new ReportExportSheet
                    {
                        Title = "Daily Sales",
                        GeneratedAt = when,
                        Headers = new() { "Date", "Amount", "Invoice Count" },
                    };
                    sheet.Rows.AddRange(rows.Select(r => new ReportExportRow(
                        new object?[] { r.Date.ToString("yyyy-MM-dd"), r.Amount.ToString("N2"), r.InvoiceCount.ToString() })));
                    sheet.Rows.Add(new ReportExportRow(new object?[]
                    {
                        "Total", rows.Sum(r => r.Amount).ToString("N2"), rows.Sum(r => r.InvoiceCount).ToString(),
                    }, isTotal: true));
                    return (sheet, null);
                }

                case ScheduledReportSetting.ReportKeyProfitLoss:
                {
                    var pl = await reports.GetProfitLossReportAsync(from7, yesterday);
                    var sheet = new ReportExportSheet
                    {
                        Title = "Profit & Loss (7 days)",
                        GeneratedAt = when,
                        Headers = new() { "Month", "Revenue", "COGS", "Expenses", "Profit" },
                    };
                    sheet.Rows.AddRange(pl.MonthlySummaries.Select(m => new ReportExportRow(
                        new object?[] { m.Month, m.Revenue.ToString("N2"), m.COGS.ToString("N2"), m.Expenses.ToString("N2"), m.Profit.ToString("N2") })));
                    sheet.Rows.Add(new ReportExportRow(new object?[]
                    {
                        "Total", pl.TotalRevenue.ToString("N2"), pl.TotalCOGS.ToString("N2"),
                        pl.TotalExpenses.ToString("N2"), pl.NetProfit.ToString("N2"),
                    }, isTotal: true));
                    return (sheet, null);
                }

                case ScheduledReportSetting.ReportKeyStockAudit:
                {
                    var rows = await reports.GetStockAuditAsync();
                    var sheet = new ReportExportSheet
                    {
                        Title = "Stock Audit",
                        GeneratedAt = when,
                        Headers = new() { "Product", "Stock", "Min Qty", "Low Stock", "Batch", "Expiry" },
                    };
                    sheet.Rows.AddRange(rows.Select(r => new ReportExportRow(new object?[]
                    {
                        r.ProductName,
                        r.StockQty.ToString(),
                        r.MinStockQty.ToString(),
                        r.IsLowStock ? "LOW" : string.Empty,
                        r.Batch ?? string.Empty,
                        r.ExpiryDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                    })));
                    return (sheet, null);
                }

                case ScheduledReportSetting.ReportKeyReceivables:
                {
                    var rows = await reports.GetReceivablesAsync();
                    var sheet = new ReportExportSheet
                    {
                        Title = "Receivables",
                        GeneratedAt = when,
                        Headers = new() { "Customer", "Outstanding", "Pending Invoices" },
                    };
                    sheet.Rows.AddRange(rows.Select(r => new ReportExportRow(new object?[]
                    {
                        r.CustomerName,
                        r.OutstandingAmount.ToString("N2"),
                        r.PendingInvoices.ToString(),
                    })));
                    sheet.Rows.Add(new ReportExportRow(new object?[]
                    {
                        "Total", rows.Sum(r => r.OutstandingAmount).ToString("N2"),
                        rows.Sum(r => r.PendingInvoices).ToString(),
                    }, isTotal: true));
                    return (sheet, null);
                }

                case ScheduledReportSetting.ReportKeyGst:
                {
                    var gst = await reports.GetGstReportAsync(from7, yesterday);
                    var sheet = new ReportExportSheet
                    {
                        Title = "GST Report (7 days)",
                        GeneratedAt = when,
                        Headers = new() { "Invoice", "Date", "Party", "GSTIN", "Taxable", "Tax", "Invoice Value" },
                    };
                    sheet.Rows.AddRange(gst.B2b.Select(r => new ReportExportRow(new object?[]
                    {
                        r.InvoiceNumber,
                        r.InvoiceDate.ToString("yyyy-MM-dd"),
                        r.CustomerName,
                        r.CustomerGstin,
                        r.TaxableValue.ToString("N2"),
                        r.TaxValue.ToString("N2"),
                        r.InvoiceValue.ToString("N2"),
                    })));
                    sheet.Rows.AddRange(gst.B2c.Select(r => new ReportExportRow(new object?[]
                    {
                        r.InvoiceNumber,
                        r.InvoiceDate.ToString("yyyy-MM-dd"),
                        r.CustomerName,
                        string.Empty,
                        r.TaxableValue.ToString("N2"),
                        r.TaxValue.ToString("N2"),
                        r.InvoiceValue.ToString("N2"),
                    })));
                    sheet.Rows.Add(new ReportExportRow(new object?[]
                    {
                        "Total", string.Empty, string.Empty, string.Empty,
                        gst.B2b.Sum(r => r.TaxableValue).Add(gst.B2c.Sum(r => r.TaxableValue)).ToString("N2"),
                        gst.B2b.Sum(r => r.TaxValue).Add(gst.B2c.Sum(r => r.TaxValue)).ToString("N2"),
                        gst.B2b.Sum(r => r.InvoiceValue).Add(gst.B2c.Sum(r => r.InvoiceValue)).ToString("N2"),
                    }, isTotal: true));
                    return (sheet, null);
                }

                default:
                    return (null, $"Unknown report key '{s.ReportKey}'.");
            }
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string DescribeRange(string reportKey) => reportKey switch
    {
        ScheduledReportSetting.ReportKeyStockAudit or
        ScheduledReportSetting.ReportKeyReceivables => "current snapshot",
        ScheduledReportSetting.ReportKeyGst => "last 7 days ending yesterday",
        _ => "last 7 days ending yesterday",
    };

    private static object Snapshot(ScheduledReportSetting s) => new
    {
        s.IsEnabled,
        s.ReportKey,
        s.ReportTitle,
        s.RecipientEmail,
        s.Subject,
        s.RunAtHour,
        s.RunAtMinute,
        s.DayOfWeek,
        s.NextRunAt
    };

    private void UpdateCache(ScheduledReportSetting s)
    {
        _lastRunAt = s.LastRunAt;
        _nextRunAt = s.NextRunAt;
        _lastResult = s.LastResult;
    }
}
