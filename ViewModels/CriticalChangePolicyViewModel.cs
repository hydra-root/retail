using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.Generic;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-05: admin screen for the separate critical-change passcode — the master switch,
/// the five gates it guards, the passcode itself (twice-entry, hashed by the service),
/// the attempt/lockout state with a manual reset, and a side-effect-free "Test passcode".
/// Nothing here stores a secret: the typed values go straight to
/// <see cref="ICriticalChangeGuard" />, which persists only the PBKDF2 hash.
/// </summary>
public partial class CriticalChangePolicyViewModel : BaseViewModel
{
    private readonly ICriticalChangeGuard _guard;

    /// <summary>The persisted row, kept only so Id/audit provenance survive a reload.</summary>
    private CriticalChangeSetting _loaded = new();

    private bool _loading;
    private string _savedSignature = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireInvoiceDelete = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireWriteOff = true;

    /// <summary>Estimate conversion ships unarmed — it is an everyday billing step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireEstimateConversion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireLockedPeriodEdit = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireStockAdjustment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _requireFinancialYearLock = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _selectedAttemptLimit = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _selectedLockoutMinutes = 15;

    [ObservableProperty]
    private string _passcodeStateText = "not set yet";

    [ObservableProperty]
    private string _lockoutStateText = string.Empty;

    [ObservableProperty]
    private bool _hasLockoutState;

    [ObservableProperty]
    private string _testResultText = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // Set by the view's code-behind: a PasswordBox cannot be data-bound, so the secrets are
    // pushed in on click exactly like UsersView does for NewPassword.
    public string CurrentPasscode { get; set; } = string.Empty;
    public string NewPasscode { get; set; } = string.Empty;
    public string ConfirmPasscode { get; set; } = string.Empty;
    public string TestPasscode { get; set; } = string.Empty;

    /// <summary>Which on-screen passcode fields the view must wipe after a write or test.</summary>
    public enum PasscodeFieldScope
    {
        /// <summary>Only the throwaway "try a passcode" box.</summary>
        TestOnly,

        /// <summary>Every box, including current / new / confirm.</summary>
        All
    }

    /// <summary>Raised after a passcode write so the view wipes the PasswordBoxes (they cannot be data-bound).</summary>
    public event System.EventHandler<PasscodeFieldScope>? ClearPasscodeBoxesRequested;

    public List<int> AttemptLimitOptions => _guard.AttemptLimitOptions;
    public List<int> LockoutMinutesOptions => _guard.LockoutMinutesOptions;

    public string InvoiceDeleteGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.InvoiceDelete);
    public string WriteOffGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.WriteOff);
    public string EstimateConversionGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.EstimateConversion);
    public string LockedPeriodEditGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.LockedPeriodEdit);
    public string StockAdjustmentGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.StockAdjustment);
    public string FinancialYearLockGate => CriticalChangeSetting.DescribeGate(CriticalChangeKind.FinancialYearLock);

    /// <summary>Live preview of the draft policy (unsaved values), mirroring the backup scheduler editor.</summary>
    public string Summary => CriticalChangeSetting.Summarize(IsEnabled, RequireInvoiceDelete, RequireWriteOff,
        RequireEstimateConversion, RequireLockedPeriodEdit, RequireStockAdjustment,
        RequireFinancialYearLock, SelectedAttemptLimit, SelectedLockoutMinutes);

    public bool IsDirty => EditSignature() != _savedSignature;

    public CriticalChangePolicyViewModel(ICriticalChangeGuard guard)
    {
        _guard = guard;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        _loading = true;
        IsLoading = true;
        try
        {
            var settings = await _guard.GetSettingsAsync();
            _loaded = settings;

            IsEnabled = settings.IsEnabled;
            RequireInvoiceDelete = settings.RequireForInvoiceDelete;
            RequireWriteOff = settings.RequireForWriteOff;
            RequireEstimateConversion = settings.RequireForEstimateConversion;
            RequireLockedPeriodEdit = settings.RequireForLockedPeriodEdit;
            RequireStockAdjustment = settings.RequireForStockAdjustment;
            RequireFinancialYearLock = settings.RequireForFinancialYearLock;
            SelectedAttemptLimit = Nearest(settings.MaxFailedAttempts, _guard.AttemptLimitOptions, 5);
            SelectedLockoutMinutes = Nearest(settings.LockoutMinutes, _guard.LockoutMinutesOptions, 15);
            _savedSignature = EditSignature();

            PasscodeStateText = DescribePasscode(settings);
            LockoutStateText = await _guard.LockoutStateTextAsync();
            HasLockoutState = settings.FailedAttempts > 0 || settings.IsLockedOut;

            ErrorMessage = string.Empty;
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Failed to load the critical-change policy: {ex.Message}";
        }
        finally
        {
            _loading = false;
            IsLoading = false;
        }
    }

    private static string DescribePasscode(CriticalChangeSetting s)
    {
        if (!s.HasPasscode)
            return "Not set — every armed gate fails closed until one is stored.";

        var changed = s.LastChangedAt is { } at
            ? $"last changed {at.ToLocalTime():dd-MMM-yyyy HH:mm} by {s.LastChangedBy ?? "unknown"}"
            : "stored (no change timestamp recorded)";
        return $"Set — {changed}. Stored as a PBKDF2 hash; the passcode itself is never kept.";
    }

    private string EditSignature() => string.Join('|',
        IsEnabled, RequireInvoiceDelete, RequireWriteOff, RequireEstimateConversion,
        RequireLockedPeriodEdit, RequireStockAdjustment, RequireFinancialYearLock,
        SelectedAttemptLimit, SelectedLockoutMinutes);

    /// <summary>
    /// The stored number need not be one of the offered choices (e.g. set by an older build),
    /// so snap it to the nearest option to keep the ComboBox selection valid.
    /// </summary>
    private static int Nearest(int value, List<int> options, int fallback)
    {
        if (options.Count == 0) return fallback;
        if (options.Contains(value)) return value;
        return options.OrderBy(o => System.Math.Abs(o - value)).First();
    }

    // ---- policy flags ----

    [RelayCommand]
    private async Task SavePolicyAsync()
    {
        var (ok, error) = await _guard.SavePolicyAsync(
            IsEnabled,
            RequireInvoiceDelete,
            RequireWriteOff,
            RequireEstimateConversion,
            RequireLockedPeriodEdit,
            RequireStockAdjustment,
            RequireFinancialYearLock,
            SelectedAttemptLimit,
            SelectedLockoutMinutes);

        if (ok)
        {
            SuccessMessage = "Critical-change policy saved and audited.";
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = error ?? "Failed to save the critical-change policy.";
            SuccessMessage = string.Empty;
        }

        await LoadDataAsync();
    }

    // ---- passcode ----

    [RelayCommand]
    private async Task SavePasscodeAsync()
    {
        if (!string.Equals(NewPasscode, ConfirmPasscode, System.StringComparison.Ordinal))
        {
            ErrorMessage = "The two passcode entries do not match — nothing was saved.";
            SuccessMessage = string.Empty;
            WipePasscodeFields();
            return;
        }

        var (ok, error) = await _guard.SetPasscodeAsync(NewPasscode, CurrentPasscode);
        if (ok)
        {
            SuccessMessage = "Critical-change passcode updated (stored as a PBKDF2 hash) and audited.";
            ErrorMessage = string.Empty;
            TestResultText = string.Empty;
        }
        else
        {
            ErrorMessage = error ?? "Failed to update the critical-change passcode.";
            SuccessMessage = string.Empty;
        }

        WipePasscodeFields();
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ClearPasscodeAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Remove the critical-change passcode? While it is removed, every armed gate refuses " +
            "the action until a new passcode is set. The master switch itself stays as saved.",
            "Remove Passcode", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var (ok, error) = await _guard.ClearPasscodeAsync(CurrentPasscode);
        if (ok)
        {
            SuccessMessage = "Critical-change passcode removed and audited.";
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = error ?? "Failed to remove the critical-change passcode.";
            SuccessMessage = string.Empty;
        }

        WipePasscodeFields();
        await LoadDataAsync();
    }

    /// <summary>
    /// Runs a hash comparison only: no attempt counting, no lockout, no audit record —
    /// so an operator can check the secret is right without risking a lockout.
    /// </summary>
    [RelayCommand]
    private async Task TestGateAsync()
    {
        var (passed, message) = await _guard.TestPasscodeAsync(TestPasscode);
        TestResultText = message ?? (passed ? "Passcode accepted." : "Passcode refused.");
        SuccessMessage = passed ? string.Empty : SuccessMessage;
        // Only the throwaway box is wiped — the draft in new/confirm must survive a test.
        WipePasscodeFields(PasscodeFieldScope.TestOnly);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ResetLockoutAsync()
    {
        var (ok, error) = await _guard.ResetLockoutAsync();
        if (ok)
        {
            SuccessMessage = "Failed attempts cleared and the gate reopened; the reset is audited.";
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = error ?? "Failed to reset the lockout.";
            SuccessMessage = string.Empty;
        }

        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        SuccessMessage = string.Empty;
        TestResultText = string.Empty;
        WipePasscodeFields();
        await LoadDataAsync();
    }

    /// <summary>Drops the in-memory copies of the secrets and asks the view to clear the boxes.</summary>
    private void WipePasscodeFields(PasscodeFieldScope scope = PasscodeFieldScope.All)
    {
        TestPasscode = string.Empty;

        if (scope == PasscodeFieldScope.All)
        {
            CurrentPasscode = string.Empty;
            NewPasscode = string.Empty;
            ConfirmPasscode = string.Empty;
        }

        ClearPasscodeBoxesRequested?.Invoke(this, scope);
    }

    /// <summary>Handy for the header: the row the policy currently lives on.</summary>
    public int PolicyRowId => _loaded.Id == 0 ? 1 : _loaded.Id;
}
