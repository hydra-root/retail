using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-11: the cheque register screen — a bank column a shopkeeper can actually run.
///
/// The toolbar filters (direction / status / post-dated / date range) go through the service, so the
/// cached window always matches them; the free-text search then filters that window client-side so
/// typing stays instant. The KPI cards are computed from exactly the rows shown, so a card and the
/// grid underneath can never disagree.
///
/// Every refusal the service makes (cannot clear twice, bounced is final, presentment date absurd,
/// locked financial year…) arrives in <see cref="LastValidationError"/> and is rendered as the
/// "Why is this blocked?" line in plain language.
/// </summary>
public partial class ChequeRegisterViewModel : BaseViewModel
{
    private const decimal Epsilon = 0.01m;

    private readonly IChequeService _chequeService;

    /// <summary>The rows currently shown (the filtered window); the cache lives in <see cref="_allRows"/>.</summary>
    [ObservableProperty]
    private ObservableCollection<Cheque> _rows = new();

    /// <summary>Everything the service returned for the current date/direction/status filter.</summary>
    private List<Cheque> _allRows = new();

    [ObservableProperty]
    private Cheque? _selectedRow;

    [ObservableProperty]
    private ChequeRegisterKpis _kpis = new();

    // ---- toolbar ----

    [ObservableProperty]
    private DateTime _startDate = FinancialYearStart();

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _directionFilter;

    [ObservableProperty]
    private string _statusFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Post-dated only: keeps cheques whose presentment date has not arrived.</summary>
    [ObservableProperty]
    private bool _postDatedOnly;

    // ---- add / edit form (EditingChequeId == 0 means "new") ----

    [ObservableProperty]
    private int _editingChequeId;

    [ObservableProperty]
    private string _newDirection = Cheque.DirectionIn;

    [ObservableProperty]
    private string _newPartyType = Cheque.PartyCustomer;

    [ObservableProperty]
    private string _newPartyName = string.Empty;

    [ObservableProperty]
    private string _newChequeNumber = string.Empty;

    [ObservableProperty]
    private string _newBankName = string.Empty;

    [ObservableProperty]
    private DateTime _newIssueDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _newPresentmentDate = DateTime.Today;

    [ObservableProperty]
    private decimal _newAmount;

    /// <summary>Opt-in per cheque: raise the PAY-03 receipt automatically when this cheque clears.</summary>
    [ObservableProperty]
    private bool _newCreateReceiptOnClear;

    [ObservableProperty]
    private string _newNotes = string.Empty;

    // ---- bounce reason (picked/typed here, consumed by the row's "Mark Bounced" button) ----

    [ObservableProperty]
    private string _bounceReason = Cheque.BounceReasonOptions[0];

    // ---- messages ----

    /// <summary>The service's plain-language refusal, shown as "Why is this blocked?".</summary>
    [ObservableProperty]
    private string _lastValidationError = string.Empty;

    [ObservableProperty]
    private string _warningText = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public List<string> Directions => Cheque.DirectionOptions;

    public List<string> PartyTypes => Cheque.PartyTypeOptions;

    public List<string> BounceReasons => Cheque.BounceReasonOptions;

    public List<string> DirectionFilters => _chequeService.DirectionFilters;

    public List<string> StatusFilters => _chequeService.StatusFilters;

    /// <summary>PDC window shown in the form hint, straight from the model so the two cannot drift.</summary>
    public int PdcWindowMonths => Cheque.PdcWindowMonths;

    public string FormHeading => EditingChequeId == 0 ? "Add a cheque" : $"Edit cheque #{EditingChequeId}";

    public bool IsNewCheque => EditingChequeId == 0;

    public bool HasRows => Rows.Count > 0;

    /// <summary>True when the grid itself is blank, so the view can show the honest empty state instead.</summary>
    public bool ShowEmptyState => Rows.Count == 0;

    /// <summary>True when the register holds nothing at all (as opposed to "nothing matches the filter").</summary>
    public bool RegisterIsEmpty => _allRows.Count == 0;

    /// <summary>Filters hid rows that do exist, so the empty state may offer "clear the filters".</summary>
    public bool ShowClearFilters => !RegisterIsEmpty;

    public string EmptyStateText => RegisterIsEmpty
        ? "No cheques in this window yet. Add the leaf in the form on the right — for a post-dated cheque, put "
          + "the date written on its face in the Presentment date box, and the register will tell you when to "
          + "present it."
        : "Nothing matches these filters. Widen the date range, set the filters back to \"All\", or clear the "
          + "Post-dated only box.";

    public string FilterSummary =>
        $"{Rows.Count} of {_allRows.Count} cheque(s) shown · " +
        $"{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy} · {DirectionFilter} · {StatusFilter}" +
        (PostDatedOnly ? " · post-dated only" : string.Empty);

    public ChequeRegisterViewModel(IChequeService chequeService)
    {
        _chequeService = chequeService;
        DirectionFilter = _chequeService.DirectionFilters[0];
        StatusFilter = _chequeService.StatusFilters[0];

        // From here on a filter change means the operator picked something, so it may re-query.
        _filtersLive = true;
    }

    // ---------------------------------------------------------------- loading

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        await RefreshAsync();
    }

    /// <summary>
    /// Re-queries the service with the toolbar's server-side filters (date range, direction, status,
    /// post-dated) and rebuilds the grid, the KPI cards and the empty state from that window.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            _allRows = await _chequeService.GetChequesAsync(
                from: StartDate,
                to: EndDate,
                direction: DirectionFilter,
                status: StatusFilter,
                postDatedOnly: PostDatedOnly);

            RebuildRows();
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"The register could not be loaded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Applies the live search to the cached window and recomputes the cards (no round trip).</summary>
    private void RebuildRows()
    {
        var needle = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

        var visible = needle == null
            ? _allRows
            : _allRows.Where(c =>
                c.ChequeNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || c.PartyName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (c.BankName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.PaymentNumber?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.BounceReason?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.Notes?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        Rows = new ObservableCollection<Cheque>(visible);
        Kpis = _chequeService.BuildKpis(visible);
    }

    partial void OnRowsChanged(ObservableCollection<Cheque> value)
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(RegisterIsEmpty));
        OnPropertyChanged(nameof(ShowClearFilters));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(FilterSummary));
    }

    partial void OnEditingChequeIdChanged(int value)
    {
        OnPropertyChanged(nameof(FormHeading));
        OnPropertyChanged(nameof(IsNewCheque));
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();

    // The pickers and the date boxes change what the QUERY returns, so they re-load. Guarded because
    // the constructor seeds the two filter values and the first real load comes from LoadDataAsync.
    private bool _filtersLive;

    partial void OnDirectionFilterChanged(string value)
    {
        if (_filtersLive) _ = RefreshAsync();
    }

    partial void OnStatusFilterChanged(string value)
    {
        if (_filtersLive) _ = RefreshAsync();
    }

    partial void OnPostDatedOnlyChanged(bool value)
    {
        if (_filtersLive) _ = RefreshAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (_filtersLive) _ = RefreshAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (_filtersLive) _ = RefreshAsync();
    }

    /// <summary>Direction drives the party vocabulary, exactly like the receipts screen.</summary>
    partial void OnNewDirectionChanged(string value)
    {
        NewPartyType = value == Cheque.DirectionIn ? Cheque.PartyCustomer : Cheque.PartySupplier;
    }

    // ---------------------------------------------------------------- form

    [RelayCommand]
    private void ClearForm()
    {
        EditingChequeId = 0;
        NewDirection = Cheque.DirectionIn; // also normalises NewPartyType back to Customer
        NewPartyType = Cheque.PartyCustomer;
        NewPartyName = string.Empty;
        NewChequeNumber = string.Empty;
        NewBankName = string.Empty;
        NewIssueDate = DateTime.Today;
        NewPresentmentDate = DateTime.Today;
        NewAmount = 0m;
        NewCreateReceiptOnClear = false;
        NewNotes = string.Empty;
        LastValidationError = string.Empty;
        SuccessMessage = string.Empty;
        ErrorMessage = string.Empty;
    }

    /// <summary>Loads an open row into the form for editing (terminal rows are refused by the service).</summary>
    [RelayCommand]
    private void EditCheque(Cheque cheque)
    {
        if (cheque == null)
        {
            LastValidationError = "Pick a row in the register first.";
            return;
        }
        if (!cheque.CanEdit)
        {
            LastValidationError = $"{cheque.ChequeNumber} is {cheque.Status}. {Cheque.Explain(cheque.Status)} "
                                + "Only Pending and Handed-over rows can be edited.";
            return;
        }

        EditingChequeId = cheque.Id;
        NewDirection = cheque.Direction;
        NewPartyType = cheque.PartyType;
        NewPartyName = cheque.PartyName;
        NewChequeNumber = cheque.ChequeNumber;
        NewBankName = cheque.BankName ?? string.Empty;
        NewIssueDate = cheque.IssueDate;
        NewPresentmentDate = cheque.PresentmentDate;
        NewAmount = cheque.Amount;
        NewCreateReceiptOnClear = cheque.CreateReceiptOnClear;
        NewNotes = cheque.Notes ?? string.Empty;
        LastValidationError = string.Empty;
        SuccessMessage = $"Editing {cheque.RowLabel}. Status does not change here — use the row's buttons.";
    }

    /// <summary>Adds a new row or saves the row loaded by <see cref="EditChequeCommand"/>.</summary>
    [RelayCommand]
    private async Task SaveChequeAsync()
    {
        var cheque = new Cheque
        {
            Id = EditingChequeId,
            Direction = NewDirection,
            PartyType = NewPartyType,
            PartyName = NewPartyName?.Trim() ?? string.Empty,
            ChequeNumber = NewChequeNumber?.Trim() ?? string.Empty,
            BankName = string.IsNullOrWhiteSpace(NewBankName) ? null : NewBankName.Trim(),
            IssueDate = NewIssueDate.Date,
            PresentmentDate = NewPresentmentDate.Date,
            Amount = NewAmount,
            CreateReceiptOnClear = NewCreateReceiptOnClear,
            Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim(),
        };

        var isNew = EditingChequeId == 0;
        var label = $"{cheque.DirectionLabel} cheque {cheque.ChequeNumber} of {cheque.Amount:C} for {cheque.PartyName}";

        // Screen-side guard so an obviously empty form is not sent to the service at all; the service
        // repeats every rule because it is also the API.
        if (string.IsNullOrWhiteSpace(cheque.PartyName))
        {
            LastValidationError = "Whose cheque is this? The party name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(cheque.ChequeNumber))
        {
            LastValidationError = "The cheque number printed by the bank is required.";
            return;
        }
        if (cheque.Amount < Epsilon)
        {
            LastValidationError = "Amount must be at least ₹0.01 — a ₹0 cheque only clutters the register.";
            return;
        }

        await RunMutationAsync(
            () => isNew ? _chequeService.AddChequeAsync(cheque) : _chequeService.UpdateChequeAsync(cheque),
            isNew ? $"Saved {label}." : $"{cheque.ChequeNumber} updated.",
            isNew ? "Cheque added to the register." : "Cheque updated.");

        if (string.IsNullOrEmpty(LastValidationError)) ClearForm();
    }

    // ---------------------------------------------------------------- row actions

    /// <summary>Status → Cleared, honouring the row's own auto-receipt flag (no forced money movement).</summary>
    [RelayCommand]
    private async Task MarkClearedAsync(Cheque cheque) => await RunOnRowAsync(
        cheque, "cleared", c => _chequeService.MarkClearedAsync(c.Id, raiseReceipt: null));

    /// <summary>Status → Cleared and raise the PAY-03 receipt in the same click (inbound cheques only).</summary>
    [RelayCommand]
    private async Task MarkClearedWithReceiptAsync(Cheque cheque) => await RunOnRowAsync(
        cheque, "cleared and receipted", c => _chequeService.MarkClearedAsync(c.Id, raiseReceipt: true));

    /// <summary>Status → HandedOver (the leaf changed hands; the bank has not spoken yet).</summary>
    [RelayCommand]
    private async Task MarkHandedOverAsync(Cheque cheque) => await RunOnRowAsync(
        cheque, "marked handed over", c => _chequeService.MarkHandedOverAsync(c.Id));

    /// <summary>Status → Bounced with the reason from the bounce box. Bounced is final.</summary>
    [RelayCommand]
    private async Task MarkBouncedAsync(Cheque cheque)
    {
        if (cheque == null)
        {
            LastValidationError = "Pick the cheque in the register first, then choose a bounce reason.";
            return;
        }

        await RunOnRowAsync(cheque, "bounced",
            c => _chequeService.MarkBouncedAsync(c.Id, BounceReason, DateTime.Today));
    }

    /// <summary>Status → Cancelled (wrong leaf, stop-payment, lost). Cancelled is final.</summary>
    [RelayCommand]
    private async Task CancelChequeAsync(Cheque cheque) => await RunOnRowAsync(
        cheque, "cancelled", c => _chequeService.CancelChequeAsync(c.Id));

    /// <summary>Delete — only a Pending row with no receipt behind it, which the service re-checks.</summary>
    [RelayCommand]
    private async Task DeleteChequeAsync(Cheque cheque) => await RunOnRowAsync(
        cheque, "deleted", c => _chequeService.DeleteChequeAsync(c.Id));

    // ---------------------------------------------------------------- range presets + export

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

    /// <summary>
    /// UTF-8 BOM CSV of the rows on screen, same Desktop convention as the sales register
    /// (RPT-02) — BOM first so Excel opens the rupee text without mangling it.
    /// </summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var fileName = $"ChequeRegister_{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            var csv = _chequeService.BuildCsv(Rows.ToList(), Kpis, StartDate, EndDate);

            File.WriteAllText(path, csv, new UTF8Encoding(true));
            SuccessMessage = $"Exported {Rows.Count} cheque(s) to {path}";
            LastValidationError = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>Runs one row action, then reloads and reports the service's verdict.</summary>
    private async Task RunOnRowAsync(Cheque cheque, string pastTense,
        Func<Cheque, Task<bool>> action)
    {
        if (cheque == null)
        {
            LastValidationError = "Pick a row in the register first.";
            return;
        }

        var ok = await RunMutationAsync(() => action(cheque),
            $"{cheque.ChequeNumber} {pastTense}.",
            $"{cheque.ChequeNumber} {pastTense}.");

        if (ok) SelectedRow = cheque;
    }

    /// <summary>
    /// Shared mutation path: clears the message strip, runs the service call, surfaces
    /// warnings + the plain-language refusal, then re-reads the register.
    /// </summary>
    private async Task<bool> RunMutationAsync(Func<Task<bool>> call,
        string successShort, string successLong)
    {
        IsBusy = true;
        SuccessMessage = string.Empty;
        ErrorMessage = string.Empty;
        WarningText = string.Empty;
        LastValidationError = string.Empty;

        bool ok;
        try
        {
            ok = await call();
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            ErrorMessage = $"The register could not be written: {ex.Message}";
            IsBusy = false;
            return false;
        }

        WarningText = string.Join("   ·   ", _chequeService.Warnings);

        if (ok)
        {
            SuccessMessage = successLong;
        }
        else
        {
            LastValidationError = _chequeService.LastValidationError
                ?? "The change was refused. Nothing in the register or the ledger was touched.";
            ErrorMessage = successShort + " — see the explanation below.";
        }

        try
        {
            _allRows = await _chequeService.GetChequesAsync(
                from: StartDate, to: EndDate, direction: DirectionFilter,
                status: StatusFilter, postDatedOnly: PostDatedOnly);
            RebuildRows();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"The change was applied but the list could not be re-read: {ex.Message}";
        }

        IsBusy = false;
        return ok;
    }

    private static DateTime FinancialYearStart() =>
        DateTime.Today.Month >= 4 ? new DateTime(DateTime.Today.Year, 4, 1) : new DateTime(DateTime.Today.Year - 1, 4, 1);
}
