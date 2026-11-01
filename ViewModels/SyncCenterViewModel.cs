using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-08: the sync control centre — the connection/settings/health layer that sits above the
/// STK-11 queue screen. It answers four questions on one screen: which transport will run and why,
/// whether HQ actually answers, how deep the local queue is, and what happened on the last runs.
/// Draining the queue row by row (discard, purge, payload inspection) stays on the
/// Offline / Sync Queue screen; this screen drives whole runs.
/// </summary>
public partial class SyncCenterViewModel : BaseViewModel
{
    /// <summary>History rows the grid loads; the table itself is bounded inside the service.</summary>
    private const int HistoryRows = 40;

    private readonly ISyncCenterService _syncCenter;

    // ---- connection card ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionBadgeText))]
    private bool _isLiveTransport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionGood))]
    [NotifyPropertyChangedFor(nameof(IsConnectionBad))]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionGood))]
    [NotifyPropertyChangedFor(nameof(IsConnectionBad))]
    private bool _wasProbed;
    [ObservableProperty] private string _transportName = "HQ endpoint not loaded yet";
    [ObservableProperty] private string _transportReason = "The connection has not been read yet.";
    [ObservableProperty] private string _probeDetail = "Press Test connection to ask the endpoint.";
    [ObservableProperty] private string _healthText = "Connection not checked yet";

    // ---- queue depth card ----
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _queuedCount;
    [ObservableProperty] private int _syncedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailuresText))]
    [NotifyPropertyChangedFor(nameof(HasNoFailures))]
    private int _failedCount;

    [ObservableProperty] private int _discardedCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private string _queueDepthText = "The local queue holds nothing yet.";
    [ObservableProperty] private string _oldestPendingText = "nothing waiting";
    [ObservableProperty] private string _statusBreakdownText = "no rows";
    [ObservableProperty] private string _entityBreakdownText = "no rows";
    [ObservableProperty] private string _queueDisciplineText = "auto-sync settings not read yet";

    // ---- last run card ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateText))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasEverSynced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRunSummaryText))]
    private string _lastRunText = "never";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRunSummaryText))]
    private string _lastRunOutcomeText = "no run recorded";

    [ObservableProperty] private string _lastRunDetailText = string.Empty;
    [ObservableProperty] private string _lastResultText = "No sync run recorded yet.";

    // ---- configuration / effective-settings card ----
    [ObservableProperty] private string _configurationText = "The effective configuration has not been read yet.";

    // ---- settings form (editable + saved mirrors, so IsDirty is honest) ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(LiveFieldsEnabled))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _savedIsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedBaseUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _deviceIdentifier = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedDeviceIdentifier = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _useTlsOnly = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _savedUseTlsOnly = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedBatchSizeLabel = SyncSetting.BatchSizeOptions[2];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedBatchSizeLabel = SyncSetting.BatchSizeOptions[2];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedTimeoutLabel = SyncSetting.TimeoutLabel(SyncSetting.DefaultTimeoutSeconds);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedTimeoutLabel = SyncSetting.TimeoutLabel(SyncSetting.DefaultTimeoutSeconds);

    // ---- messages / busy ----
    [ObservableProperty] private string _successMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _warningsText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestSucceededVisible))]
    [NotifyPropertyChangedFor(nameof(TestFailedVisible))]
    private string _testResultText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestSucceededVisible))]
    [NotifyPropertyChangedFor(nameof(TestFailedVisible))]
    private bool _testPassed;
    [ObservableProperty] private string _lastChangedByText = "not changed yet";
    [ObservableProperty] private bool _isBusy;

    // ---- history grid ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryRowCount))]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    [NotifyPropertyChangedFor(nameof(HasNoHistory))]
    private ObservableCollection<SyncRunLog> _history = new();

    [ObservableProperty] private SyncRunLog? _selectedRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(HasUnsavedApiKey))]
    private string? _pendingApiKey;

    /// <summary>
    /// API key typed into the PasswordBox (see the field above). It is never bound in XAML and never
    /// written back out: the box pushes it in through its PasswordChanged handler and again on the
    /// Save click, and the stored key is only ever surfaced through <see cref="ApiKeyStatusText"/>.
    /// </summary>
    public string? PendingApiKeyInput
    {
        get => PendingApiKey;
        set => PendingApiKey = value;
    }

    /// <summary>Raised so the view can wipe the PasswordBox after a save; the box is never refilled.</summary>
    public event EventHandler? ClearApiKeyBoxRequested;

    public List<string> BatchSizeOptions => SyncSetting.BatchSizeOptions;

    public List<string> TimeoutOptions => SyncSetting.TimeoutOptions;

    /// <summary>Masked reading of the stored key, e.g. "••••••••3f2a (24 characters)".</summary>
    [ObservableProperty] private string _apiKeyStatusText = "No API key stored yet.";

    /// <summary>True when the operator typed a key that has not been saved; the test says so.</summary>
    public bool HasUnsavedApiKey => !string.IsNullOrWhiteSpace(PendingApiKey);

    public bool IsDirty => IsEnabled != SavedIsEnabled
                           || !string.Equals(BaseUrl?.Trim() ?? string.Empty, SavedBaseUrl, StringComparison.Ordinal)
                           || !string.Equals(DeviceIdentifier?.Trim() ?? string.Empty, SavedDeviceIdentifier,
                                             StringComparison.Ordinal)
                           || UseTlsOnly != SavedUseTlsOnly
                           || !string.Equals(SelectedBatchSizeLabel, SavedBatchSizeLabel, StringComparison.Ordinal)
                           || !string.Equals(SelectedTimeoutLabel, SavedTimeoutLabel, StringComparison.Ordinal)
                           // A freshly pasted key alone must enable Save, or the box could never be saved.
                           || !string.IsNullOrWhiteSpace(PendingApiKey);

    /// <summary>Address/device fields only mean something while live sync is on, so the form dims them.</summary>
    public bool LiveFieldsEnabled => IsEnabled;

    /// <summary>Drives the green banner; only ever true when a probe actually succeeded.</summary>
    public bool IsConnectionGood => IsOnline && WasProbed;

    /// <summary>Drives the red banner; a screen that has not probed yet is not "bad news".</summary>
    public bool IsConnectionBad => WasProbed && !IsOnline;

    public string ConnectionBadgeText => IsLiveTransport
        ? "LIVE HTTPS TRANSPORT"
        : "SIMULATED TRANSPORT (LOCAL STAND-IN)";

    public int HistoryRowCount => History.Count;

    /// <summary>False only when the till has no recorded run at all — the grid's empty state.</summary>
    public bool HasHistory => History.Count > 0;

    /// <summary>The grid's empty state: no run has ever been recorded from this till.</summary>
    public bool HasNoHistory => History.Count == 0;

    /// <summary>Green test banner: shown only once a test has actually reported something.</summary>
    public bool TestSucceededVisible => TestPassed && !string.IsNullOrWhiteSpace(TestResultText);

    /// <summary>Red test banner: same rule, so a screen nobody has tested shows neither colour.</summary>
    public bool TestFailedVisible => !TestPassed && !string.IsNullOrWhiteSpace(TestResultText);

    /// <summary>Honest empty state of the last-run card (nothing has ever synced).</summary>
    public bool ShowEmptyState => !HasEverSynced;

    /// <summary>Calm wording of the failure card when nothing is on hold.</summary>
    public bool HasNoFailures => FailedCount == 0;

    /// <summary>Honest empty state for a till that has never talked to HQ.</summary>
    public string EmptyStateText => HasEverSynced
        ? string.Empty
        : "Nothing has synced from this till yet. The queue is working — documents you save are " +
          "recorded locally — but no run has delivered anything. Press Sync now to try, or Test " +
          "connection first if you have just set up the live endpoint.";

    public string FailuresText => FailedCount == 0
        ? "No change has run out of attempts, so every queued document is still replayable."
        : $"{FailedCount} change(s) reached the attempt ceiling and are on hold. Open the Offline / " +
          "Sync Queue screen to read each row's error, then Retry failed here or discard the row there.";

    public string LastRunSummaryText => LastRunText == "never"
        ? "No run recorded"
        : $"{LastRunText} — {LastRunOutcomeText}";

    public SyncCenterViewModel(ISyncCenterService syncCenter) => _syncCenter = syncCenter;

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            await LoadSettingsFormAsync();
            await ReloadBoardAsync(probe: true);
            History = new ObservableCollection<SyncRunLog>(await _syncCenter.GetHistoryAsync(HistoryRows));

            ErrorMessage = _syncCenter.LastValidationError ?? string.Empty;
            WarningsText = BuildWarnings();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Loading the sync centre failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsBusy = true;
        try
        {
            var (success, error) = await _syncCenter.SaveSettingsAsync(IsEnabled, BaseUrl, PendingApiKey,
                DeviceIdentifier, UseTlsOnly, SelectedBatchSizeLabel, SelectedTimeoutLabel);

            if (!success)
            {
                SuccessMessage = string.Empty;
                ErrorMessage = error ?? "Failed to save the sync connection.";
                await ReloadBoardAsync(probe: false);
                return;
            }

            // The box is wiped instead of refilled, so the secret never reappears on screen.
            PendingApiKey = null;
            ClearApiKeyBoxRequested?.Invoke(this, EventArgs.Empty);

            ErrorMessage = string.Empty;
            SuccessMessage = IsEnabled
                ? "Connection saved. The next run uses the live HQ endpoint — one Sync now proves whether " +
                  "the address, key and device name are right."
                : "Connection saved. Live sync is off, so runs keep using the simulated endpoint.";

            await LoadSettingsFormAsync();
            await ReloadBoardAsync(probe: false);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Saving the sync connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        // A test always speaks for the SAVED configuration — say so instead of pretending to try a
        // key the operator has only typed into the box.
        if (HasUnsavedApiKey)
        {
            TestPassed = false;
            TestResultText = "The API key in the box has not been saved yet, and a connection test can only " +
                             "use saved settings. Press Save connection, then Test connection.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _syncCenter.TestConnectionAsync();
            TestPassed = result.Success;
            TestResultText = result.Message;

            await ReloadBoardAsync(probe: false);
            ErrorMessage = string.Empty;
            WarningsText = BuildWarnings();
        }
        catch (Exception ex)
        {
            TestPassed = false;
            TestResultText = $"Testing the connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunNowAsync() => await RunAsync("Sync now", () => _syncCenter.RunNowAsync("Manual"));

    [RelayCommand]
    private async Task RetryFailedAsync() => await RunAsync("Retry failed", () => _syncCenter.RetryFailedAsync());

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        IsBusy = true;
        try
        {
            var (success, removed, error) = await _syncCenter.ClearHistoryAsync();
            ErrorMessage = success ? string.Empty : error ?? "Clearing the history failed.";
            SuccessMessage = success
                ? removed == 0
                    ? "There is no run history to clear."
                    : $"Cleared {removed} recorded run(s). The change queue itself is untouched."
                : string.Empty;

            History = new ObservableCollection<SyncRunLog>(await _syncCenter.GetHistoryAsync(HistoryRows));
            await ReloadBoardAsync(probe: false);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Clearing the history failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync(string label, Func<Task<OfflineSyncOutcome>> action)
    {
        IsBusy = true;
        try
        {
            var outcome = await action();
            SuccessMessage = outcome.Summary;
            ErrorMessage = _syncCenter.LastValidationError ?? string.Empty;

            await ReloadBoardAsync(probe: false);
            History = new ObservableCollection<SyncRunLog>(await _syncCenter.GetHistoryAsync(HistoryRows));
            WarningsText = BuildWarnings();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{label} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSettingsFormAsync()
    {
        var settings = await _syncCenter.GetSettingsAsync();

        IsEnabled = settings.IsEnabled;
        SavedIsEnabled = settings.IsEnabled;
        BaseUrl = settings.BaseUrl ?? string.Empty;
        SavedBaseUrl = BaseUrl;
        DeviceIdentifier = settings.DeviceIdentifier ?? string.Empty;
        SavedDeviceIdentifier = DeviceIdentifier;
        UseTlsOnly = settings.UseTlsOnly;
        SavedUseTlsOnly = UseTlsOnly;

        // Nearest vocabulary label, so each ComboBox always matches one of its items.
        SelectedBatchSizeLabel = SyncSetting.BatchSizeLabel(settings.BatchSize);
        SavedBatchSizeLabel = SelectedBatchSizeLabel;
        SelectedTimeoutLabel = SyncSetting.TimeoutLabel(settings.TimeoutSeconds);
        SavedTimeoutLabel = SelectedTimeoutLabel;

        // Masked only — the stored key is never written back into the PasswordBox.
        ApiKeyStatusText = settings.HasApiKey
            ? $"Stored key: {SyncSetting.Mask(settings.ApiKey)} ({settings.ApiKey.Trim().Length} characters). " +
              "Leave the box empty to keep it, or paste a new one to replace it."
            : "No API key stored yet — live sync needs the key HQ issued for this till.";

        LastChangedByText = string.IsNullOrWhiteSpace(settings.UpdatedBy)
            ? "not changed yet"
            : $"{settings.UpdatedBy} on {settings.UpdatedAt.ToLocalTime():dd-MMM-yyyy HH:mm}";

        // The box is wiped instead of refilled: a reload must never put a secret back on screen, and
        // stale typed text must not linger into the next save.
        if (!string.IsNullOrWhiteSpace(PendingApiKey))
            ClearApiKeyBoxRequested?.Invoke(this, EventArgs.Empty);

        PendingApiKey = null;
    }

    /// <summary>Cards + configuration text, without touching the saved settings.</summary>
    private async Task ReloadBoardAsync(bool probe)
    {
        var snapshot = await _syncCenter.GetSnapshotAsync(probe);

        TransportName = snapshot.TransportName;
        IsLiveTransport = snapshot.IsLiveTransport;
        TransportReason = snapshot.TransportReason;
        ProbeDetail = snapshot.ProbeDetail;
        HealthText = snapshot.HealthText;
        IsOnline = snapshot.IsOnline;
        WasProbed = snapshot.WasProbed;
        ConfigurationText = snapshot.ConfigurationText;

        var queue = snapshot.Queue;
        TotalCount = queue.Total;
        QueuedCount = queue.Queued;
        SyncedCount = queue.Synced;
        FailedCount = queue.Failed;
        DiscardedCount = queue.Discarded;
        PendingCount = queue.PendingCount;
        QueueDepthText = snapshot.QueueDepthText;
        OldestPendingText = queue.OldestQueuedAt == null
            ? "nothing waiting"
            : $"oldest change waiting {queue.OldestPendingAge} (since {queue.OldestQueuedAt.Value.ToLocalTime():dd-MMM HH:mm})";
        StatusBreakdownText = snapshot.StatusBreakdownLines.Count == 0
            ? "no rows"
            : string.Join("   ·   ", snapshot.StatusBreakdownLines);
        EntityBreakdownText = snapshot.EntityBreakdownLines.Count == 0
            ? "no rows"
            : string.Join("   ·   ", snapshot.EntityBreakdownLines);
        QueueDisciplineText = queue.IsForcedOffline
            ? "forced offline ON — nothing is sent, however good the link is"
            : queue.AutoSyncEnabled
                ? $"forced offline OFF, auto-sync {OfflineSetting.IntervalLabel(queue.AutoSyncMinutes).ToLowerInvariant()}, " +
                  $"ceiling {Math.Max(1, queue.MaxAttempts)} attempt(s) per change"
                : "forced offline OFF, auto-sync off — replay only on demand";

        HasEverSynced = snapshot.HasEverSynced;
        ApplyLastRun(snapshot.LastRun);
    }

    private void ApplyLastRun(SyncRunLog? run)
    {
        if (run == null)
        {
            LastRunText = "never";
            LastRunOutcomeText = "no run recorded by this screen";
            LastRunDetailText = string.Empty;
            LastResultText = "No sync run recorded yet — the Offline / Sync Queue screen still shows every " +
                             "run it has driven.";
            return;
        }

        LastRunText = run.StartedText;
        LastRunOutcomeText = $"{run.Outcome} in {run.DurationText} ({run.TransportName})";
        LastRunDetailText = $"{run.CountText}, {run.RejectedCount} rejected, {run.FailedCount} newly failed, " +
                            $"{run.RemainingCount} left queued — trigger {run.Trigger}, {run.StoreLabel}, " +
                            $"device {run.DeviceLabel}";
        LastResultText = string.IsNullOrWhiteSpace(run.Error) ? run.OutcomeHint : run.Error;
    }

    private string BuildWarnings()
    {
        var problems = _syncCenter.Warnings;
        if (problems.Count == 0) return string.Empty;
        return "Note: " + string.Join(" | ", problems.Take(3));
    }

    partial void OnSelectedRunChanged(SyncRunLog? value)
    {
        if (value == null) return;
        LastRunOutcomeText = $"{value.Outcome} in {value.DurationText} ({value.TransportName})";
        LastRunDetailText = $"{value.CountText}, {value.RejectedCount} rejected, {value.FailedCount} newly " +
                            $"failed, {value.RemainingCount} left queued — trigger {value.Trigger}, " +
                            $"{value.StoreLabel}, device {value.DeviceLabel}";
        LastResultText = string.IsNullOrWhiteSpace(value.Error) ? value.OutcomeHint : value.Error;
    }
}
