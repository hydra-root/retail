using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class JournalViewModel : BaseViewModel
{
    private readonly IJournalService _journalService;
    private readonly IAccountService _accountService;

    [ObservableProperty]
    private ObservableCollection<JournalEntry> _entries = new();

    [ObservableProperty]
    private JournalEntry _selectedEntry = null!;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // New entry form
    [ObservableProperty]
    private int _newAccountId;

    [ObservableProperty]
    private decimal _newDebit;

    [ObservableProperty]
    private decimal _newCredit;

    [ObservableProperty]
    private string _newNarration = string.Empty;

    [ObservableProperty]
    private ObservableCollection<JournalLine> _newLines = new();

    [ObservableProperty]
    private Account _selectedAccountForLine = null!;

    // ACC-03: user-controlled entry date + header narration
    [ObservableProperty]
    private DateTime _newEntryDate = DateTime.Today;

    [ObservableProperty]
    private string _newEntryNarration = string.Empty;

    public List<Account> Accounts { get; set; } = new();

    public JournalViewModel(IJournalService journalService, IAccountService accountService)
    {
        _journalService = journalService;
        _accountService = accountService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var entries = await _journalService.GetAllJournalEntriesAsync();
        Entries = new ObservableCollection<JournalEntry>(entries);
        var accounts = await _accountService.GetAllAccountsAsync();
        Accounts = accounts;
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private void AddLine()
    {
        var account = Accounts.FirstOrDefault(a => a.Id == NewAccountId);
        if (account == null)
        {
            ErrorMessage = "Please select a valid account.";
            return;
        }

        if (NewDebit <= 0 && NewCredit <= 0)
        {
            ErrorMessage = "Debit or Credit amount must be greater than zero.";
            return;
        }

        NewLines.Add(new JournalLine
        {
            AccountId = NewAccountId,
            Account = account,
            Debit = NewDebit,
            Credit = NewCredit,
            Narration = NewNarration
        });

        NewDebit = 0;
        NewCredit = 0;
        NewNarration = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void RemoveLine(JournalLine line)
    {
        NewLines.Remove(line);
    }

    [RelayCommand]
    private async Task PostEntryAsync()
    {
        if (NewLines.Count == 0)
        {
            ErrorMessage = "Please add at least one journal line.";
            return;
        }

        var totalDebit = NewLines.Sum(l => l.Debit);
        var totalCredit = NewLines.Sum(l => l.Credit);

        if (totalDebit != totalCredit)
        {
            ErrorMessage = $"Journal entry is not balanced. Debits {totalDebit:N2} must equal credits {totalCredit:N2}.";
            return;
        }

        var entry = new JournalEntry
        {
            // ACC-03: keep the user's date and narration instead of stamping now
            EntryDate = NewEntryDate,
            Narration = string.IsNullOrWhiteSpace(NewEntryNarration)
                ? "Manual journal entry"
                : NewEntryNarration.Trim(),
            ReferenceType = "Manual",
            Lines = NewLines.ToList()
        };

        var success = await _journalService.PostJournalEntryAsync(entry);
        if (success)
        {
            NewLines.Clear();
            NewEntryNarration = string.Empty;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            // ACC-03: surface the service's precise validation reason
            ErrorMessage = _journalService.LastValidationError ?? "Failed to post journal entry.";
        }
    }
}
