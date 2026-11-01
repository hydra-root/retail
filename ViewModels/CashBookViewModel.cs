using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-10: cash &amp; bank book - combined or per-account book, contra transfers.
/// </summary>
public partial class CashBookViewModel : BaseViewModel
{
    private readonly ICashBookService _cashBookService;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    /// <summary>Null selection = combined book of all cash/bank accounts.</summary>
    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<CashBookRow> _rows = new();

    [ObservableProperty]
    private CashBookReport _report = new();

    // New transfer form
    [ObservableProperty]
    private bool _isTransferFormVisible;

    [ObservableProperty]
    private Account? _transferFrom;

    [ObservableProperty]
    private Account? _transferTo;

    [ObservableProperty]
    private string _transferAmountText = string.Empty;

    [ObservableProperty]
    private DateTime _transferDate = DateTime.Today;

    [ObservableProperty]
    private string _transferNarration = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public CashBookViewModel(ICashBookService cashBookService)
    {
        _cashBookService = cashBookService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var accounts = await _cashBookService.GetCashBankAccountsAsync();
            Accounts = new ObservableCollection<Account>(accounts);
            await GenerateAsync();
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
    private async Task GenerateAsync()
    {
        try
        {
            var result = await _cashBookService.GetCashBookAsync(
                SelectedAccount?.Id, StartDate, EndDate);
            Report = result;
            Rows = new ObservableCollection<CashBookRow>(result.Rows);
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnSelectedAccountChanged(Account? value) => _ = GenerateAsync();

    [RelayCommand]
    private void ToggleTransferForm() => IsTransferFormVisible = !IsTransferFormVisible;

    /// <summary>PAY-10: post a contra transfer between two cash/bank accounts.</summary>
    [RelayCommand]
    private async Task SaveTransferAsync()
    {
        if (TransferFrom == null || TransferTo == null)
        {
            ErrorMessage = "Choose both the source and the destination account.";
            return;
        }
        if (!decimal.TryParse(TransferAmountText, out var amount))
        {
            ErrorMessage = "Enter a valid transfer amount.";
            return;
        }

        var (ok, error) = await _cashBookService.TransferAsync(
            TransferFrom.Id, TransferTo.Id, amount, TransferDate, TransferNarration);
        if (ok)
        {
            SuccessMessage = $"Transferred {amount:C} from {TransferFrom.Name} to {TransferTo.Name}.";
            ErrorMessage = string.Empty;
            TransferAmountText = string.Empty;
            TransferNarration = string.Empty;
            IsTransferFormVisible = false;
            await GenerateAsync();
        }
        else
        {
            ErrorMessage = error ?? "Transfer failed.";
        }
    }

    /// <summary>PAY-10: export the cash book to a CSV file on Desktop.</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var scope = SelectedAccount?.Name ?? "AllAccounts";
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"CashBook_{scope}_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Cash Book ({scope}),{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine($"Opening Balance,{Report.OpeningBalance:C}");
            sb.AppendLine();
            sb.AppendLine("Date,Account,Type,Reference,Narration,In,Out,Balance");
            foreach (var r in Rows)
                sb.AppendLine($"{r.Date:yyyy-MM-dd},{Csv(r.AccountName)},{Csv(r.Type)},{Csv(r.Reference)},{Csv(r.Narration)},{r.In},{r.Out},{r.RunningBalance}");
            sb.AppendLine($"Closing,,{Report.ClosingBalance:C}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
