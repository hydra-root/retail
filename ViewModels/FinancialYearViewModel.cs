using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class FinancialYearViewModel : BaseViewModel
{
    private readonly IFinancialYearService _fyService;
    private readonly ICriticalChangeGuard _criticalChangeGuard;

    [ObservableProperty]
    private ObservableCollection<FinancialYear> _financialYears = new();

    [ObservableProperty]
    private FinancialYear _selectedFy = null!;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private DateTime _newStartDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _newEndDate = DateTime.Today.AddYears(1).AddDays(-1);

    [ObservableProperty]
    private bool _newIsActive;

    [ObservableProperty]
    private bool _newIsLocked;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public FinancialYearViewModel(IFinancialYearService fyService, ICriticalChangeGuard criticalChangeGuard)
    {
        _fyService = fyService;
        _criticalChangeGuard = criticalChangeGuard;
    }

    /// <summary>
    /// SEC-05: the separate critical-change passcode. Returns true when the action may proceed.
    /// </summary>
    private async Task<bool> ConfirmCriticalChangeAsync(CriticalChangeKind kind, string action, string? target = null)
    {
        if (!await _criticalChangeGuard.IsRequiredAsync(kind))
            return true;                       // master switch off, or this gate not armed

        string? refusal = null;
        DateTime? lockedUntilUtc = null;
        int remaining = 0;

        while (true)
        {
            var prompt = Views.CriticalChangeDialog.Show(
                action, CriticalChangeSetting.DescribeGate(kind), refusal, lockedUntilUtc, remaining);
            if (prompt == null) return false;  // operator cancelled

            var outcome = await _criticalChangeGuard.VerifyAsync(kind, prompt.Passcode, prompt.Reason, target);
            if (outcome.Allowed)
            {
                ErrorMessage = string.Empty;
                return true;
            }

            refusal = outcome.Message;
            lockedUntilUtc = outcome.LockedUntilUtc;
            remaining = outcome.RemainingAttempts;
        }
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var fys = await _fyService.GetAllFinancialYearsAsync();
        FinancialYears = new ObservableCollection<FinancialYear>(fys);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddFinancialYearAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName))
        {
            ErrorMessage = "Financial year name is required.";
            return;
        }

        if (_newEndDate < _newStartDate)
        {
            ErrorMessage = "End date must be after start date.";
            return;
        }

        var fy = new FinancialYear
        {
            Name = _newName,
            StartDate = _newStartDate,
            EndDate = _newEndDate,
            IsActive = _newIsActive,
            IsLocked = _newIsLocked
        };

        var success = await _fyService.AddFinancialYearAsync(fy);
        if (success)
        {
            _newName = string.Empty;
            _newStartDate = DateTime.Today;
            _newEndDate = DateTime.Today.AddYears(1).AddDays(-1);
            _newIsActive = false;
            _newIsLocked = false;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add financial year.";
        }
    }

    [RelayCommand]
    private async Task DeleteFinancialYearAsync()
    {
        if (_selectedFy == null) return;

        var success = await _fyService.DeleteFinancialYearAsync(_selectedFy.Id);
        if (success)
        {
            _selectedFy = null!;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (_selectedFy == null) return;

        var success = await _fyService.SetActiveYearAsync(_selectedFy.Id);
        if (success)
        {
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleLockAsync()
    {
        if (_selectedFy == null) return;

        var fy = _selectedFy;
        if (fy.IsLocked)
        {
            var unlock = System.Windows.MessageBox.Show(
                $"Unlock FY {fy.Name}? Locked periods become open for posting again.",
                "Confirm Unlock", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (unlock != System.Windows.MessageBoxResult.Yes) return;
        }

        // SEC-05: opening or closing a whole period is critical in both directions.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.FinancialYearLock,
                $"{(fy.IsLocked ? "Unlock" : "Lock")} financial year {fy.Name} " +
                $"({fy.StartDate:dd-MMM-yyyy} to {fy.EndDate:dd-MMM-yyyy})",
                fy.Name))
            return;

        var newLockStatus = !fy.IsLocked;
        var success = await _fyService.LockYearAsync(fy.Id, newLockStatus);
        if (success)
        {
            await LoadDataAsync();
        }
    }

    /// <summary>ACC-07: close out the year - create + activate the next FY and roll number series forward.</summary>
    [RelayCommand]
    private async Task CarryForwardAsync()
    {
        if (_selectedFy == null)
        {
            ErrorMessage = "Select the financial year to close out first.";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Close out FY {_selectedFy.Name}? The next financial year will be created, activated, and the number series rolled over.\nRemember to lock {_selectedFy.Name} afterwards if required.",
            "Carry Forward", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var (ok, error, message) = await _fyService.CarryForwardAsync(_selectedFy.Id);
        if (ok)
        {
            SuccessMessage = message ?? "Carry forward complete.";
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = error ?? "Carry forward failed.";
        }
    }

    public IEnumerable<FinancialYear> SortedFinancialYears =>
        FinancialYears.OrderByDescending(f => f.StartDate);
}
