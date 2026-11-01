using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-08: wizard for adjusting account opening balances, with a per-type summary card.
/// Note: trial balance math assumes debits are positive for Assets/Expenses and
/// credits are positive for Liabilities/Income/Equity when entering opening balances.
/// </summary>
public partial class OpeningBalanceViewModel : BaseViewModel
{
    private readonly IOpeningBalanceService _openingBalanceService;

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private ObservableCollection<OpeningBalanceSummaryRow> _summaryRows = new();

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private decimal _newOpeningBalance;

    [ObservableProperty]
    private decimal _totalOpeningBalance;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public OpeningBalanceViewModel(IOpeningBalanceService openingBalanceService)
    {
        _openingBalanceService = openingBalanceService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _openingBalanceService.GetAccountsWithOpeningAsync();
            var previous = SelectedAccount;
            Accounts = new ObservableCollection<Account>(list);
            SelectedAccount = previous is null ? Accounts.FirstOrDefault() : Accounts.FirstOrDefault(a => a.Id == previous.Id);
            if (SelectedAccount is not null)
                NewOpeningBalance = SelectedAccount.OpeningBalance;

            var summary = await _openingBalanceService.GetOpeningBalancesSummaryAsync();
            SummaryRows = new ObservableCollection<OpeningBalanceSummaryRow>(summary);
            TotalOpeningBalance = summary.Sum(r => r.TotalOpeningBalance);

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

    partial void OnSelectedAccountChanged(Account? value)
    {
        if (value is not null)
            NewOpeningBalance = value.OpeningBalance;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        if (SelectedAccount is null)
        {
            ErrorMessage = "Select an account first.";
            return;
        }

        var (ok, error, difference) = await _openingBalanceService.SaveOpeningBalanceAsync(SelectedAccount.Id, NewOpeningBalance);
        if (!ok)
        {
            ErrorMessage = error ?? "Failed to save opening balance.";
            return;
        }

        var change = difference >= 0 ? $"+{difference:F2}" : $"{difference:F2}";
        SuccessMessage = $"Opening balance for '{SelectedAccount.Name}' adjusted by {change}.";
        if (difference != 0)
            SuccessMessage += " Trial balance math assumes debits positive for Assets/Expenses, credits positive for Liabilities/Income/Equity.";

        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();
}
