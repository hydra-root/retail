using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-09: Day book (all journal entries in a range) and per-account ledger
/// with running balance. One view, two modes, CSV export.
/// </summary>
public partial class LedgerBookViewModel : BaseViewModel
{
    private readonly IReportService _reportService;
    private readonly IAccountService _accountService;

    [ObservableProperty]
    private string _viewMode = "Day Book";

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private ObservableCollection<DayBookEntry> _dayBookEntries = new();

    /// <summary>Flat rows for the day book grid (one row per journal line).</summary>
    [ObservableProperty]
    private ObservableCollection<LedgerRow> _dayBookFlatRows = new();

    [ObservableProperty]
    private LedgerReport _ledgerReport = new();

    [ObservableProperty]
    private ObservableCollection<LedgerRow> _ledgerRows = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> ViewModes { get; } = new() { "Day Book", "Ledger" };

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLedgerMode));
        OnPropertyChanged(nameof(IsDayBookMode));
    }

    public bool IsLedgerMode => ViewMode == "Ledger";

    public bool IsDayBookMode => ViewMode == "Day Book";

    public LedgerBookViewModel(IReportService reportService, IAccountService accountService)
    {
        _reportService = reportService;
        _accountService = accountService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var accounts = await _accountService.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (IsLedgerMode)
        {
            if (SelectedAccount == null)
            {
                SuccessMessage = "Please select an account for the ledger.";
                return;
            }
            var ledger = await _reportService.GetLedgerAsync(SelectedAccount.Id, StartDate, EndDate);
            LedgerReport = ledger;
            LedgerRows = new ObservableCollection<LedgerRow>(ledger.Rows);
        }
        else
        {
            var dayBook = await _reportService.GetDayBookAsync(StartDate, EndDate);
            DayBookEntries = new ObservableCollection<DayBookEntry>(dayBook.Entries);
            DayBookFlatRows = new ObservableCollection<LedgerRow>(
                dayBook.Entries.SelectMany(e => e.Lines.Select(l => new LedgerRow
                {
                    Date = e.EntryDate,
                    ReferenceType = e.ReferenceType,
                    ReferenceId = e.ReferenceId,
                    Narration = string.IsNullOrWhiteSpace(e.Narration) ? l.Narration : $"{e.Narration} / {l.Narration}",
                    Debit = l.Debit,
                    Credit = l.Credit
                })));
        }
        OnPropertyChanged(nameof(DayBookTotal));
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = IsLedgerMode
                ? $"Ledger_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv"
                : $"DayBook_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            if (IsLedgerMode)
            {
                sb.AppendLine($"Ledger - {LedgerReport.AccountName},{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
                sb.AppendLine($"Opening Balance,{LedgerReport.OpeningBalance:F2}");
                sb.AppendLine($"Closing Balance,{LedgerReport.ClosingBalance:F2}");
                sb.AppendLine();
                sb.AppendLine("Date,Reference,Ref#,Narration,Debit,Credit,Running Balance");
                foreach (var r in LedgerRows)
                    sb.AppendLine($"{r.Date:dd-MMM-yyyy},{r.ReferenceType},{r.ReferenceId},{r.Narration},{r.Debit:F2},{r.Credit:F2},{r.RunningBalance:F2}");
            }
            else
            {
                sb.AppendLine($"Day Book,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
                sb.AppendLine();
                sb.AppendLine("Date,Reference,Ref#,Account,Debit,Credit,Narration");
                foreach (var e in DayBookEntries)
                    foreach (var l in e.Lines)
                        sb.AppendLine($"{e.EntryDate:dd-MMM-yyyy},{e.ReferenceType},{e.ReferenceId},{l.AccountName},{l.Debit:F2},{l.Credit:F2},{l.Narration}");
            }

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Total of the day book (total debits).</summary>
    public decimal DayBookTotal => DayBookEntries.Sum(e => e.Total);
}
