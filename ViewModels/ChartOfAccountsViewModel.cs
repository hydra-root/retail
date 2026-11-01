using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class ChartOfAccountsViewModel : BaseViewModel
{
    private readonly IAccountGroupService _groupService;
    private readonly IAccountService _accountService;

    [ObservableProperty]
    private ObservableCollection<AccountGroup> _groups = new();

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    [ObservableProperty]
    private AccountGroup _selectedGroup = null!;

    [ObservableProperty]
    private Account _selectedAccount = null!;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private string _newGroupType = string.Empty;

    [ObservableProperty]
    private string _newAccountName = string.Empty;

    [ObservableProperty]
    private string _newAccountType = string.Empty;

    [ObservableProperty]
    private int _newAccountGroupId;

    [ObservableProperty]
    private decimal _newOpeningBalance;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> AccountTypes { get; } = new() { "Asset", "Liability", "Income", "Expense", "Equity" };

    public ChartOfAccountsViewModel(IAccountGroupService groupService, IAccountService accountService)
    {
        _groupService = groupService;
        _accountService = accountService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var groups = await _groupService.GetAllAccountGroupsAsync();
        Groups = new ObservableCollection<AccountGroup>(groups);
        var accounts = await _accountService.GetAllAccountsAsync();
        Accounts = new ObservableCollection<Account>(accounts);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName) || string.IsNullOrWhiteSpace(NewGroupType))
        {
            ErrorMessage = "Group name and type are required.";
            return;
        }

        var group = new AccountGroup
        {
            Name = NewGroupName,
            Type = NewGroupType,
            IsSystem = false
        };

        var success = await _groupService.AddAccountGroupAsync(group);
        if (success)
        {
            NewGroupName = string.Empty;
            NewGroupType = string.Empty;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add group.";
        }
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAccountName) || string.IsNullOrWhiteSpace(NewAccountType) || NewAccountGroupId <= 0)
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        var account = new Account
        {
            Name = NewAccountName,
            AccountType = NewAccountType,
            GroupId = NewAccountGroupId,
            OpeningBalance = NewOpeningBalance,
            IsSystem = false
        };

        var success = await _accountService.AddAccountAsync(account);
        if (success)
        {
            NewAccountName = string.Empty;
            NewAccountType = string.Empty;
            NewAccountGroupId = 0;
            NewOpeningBalance = 0;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add account.";
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        if (SelectedAccount == null) return;
        if (SelectedAccount.IsSystem)
        {
            ErrorMessage = "System accounts cannot be deleted.";
            return;
        }

        var success = await _accountService.DeleteAccountAsync(SelectedAccount.Id);
        if (success)
        {
            await LoadDataAsync();
            SelectedAccount = null!;
        }
    }

    public IEnumerable<Account> FilteredAccounts =>
        SelectedGroup == null
            ? Accounts
            : Accounts.Where(a => a.GroupId == SelectedGroup.Id);
}
