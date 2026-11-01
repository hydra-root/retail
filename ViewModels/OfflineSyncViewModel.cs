using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-11: offline operation. Status card for the HQ link, the forced-offline / auto-sync /
/// attempt-ceiling switches, the change queue with its replay actions and a payload detail pane.
/// Local-first: nothing here blocks billing — the queue is written by the call sites and this
/// screen is how the operator drains it.
/// </summary>
public partial class OfflineSyncViewModel : BaseViewModel
{
    /// <summary>Rows the grid loads per page; the replay itself walks the queue in CreatedAt order.</summary>
    private const int PageSize = 300;

    private readonly IOfflineQueueService _offlineQueue;

    // ---- status card ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    private bool _isOnline;

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _queuedCount;
    [ObservableProperty] private int _syncedCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _discardedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _lastSyncText = "never";
    [ObservableProperty] private string _lastResultText = "No sync run recorded yet.";
    [ObservableProperty] private string _oldestPendingText = "nothing waiting";
    [ObservableProperty] private string _nextUpText = "queue is empty";
    [ObservableProperty] private string _autoSyncText = "auto-sync off";

    // ---- settings form (editable + saved mirrors, so IsDirty is honest) ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private bool _isForcedOffline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _savedIsForcedOffline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private bool _autoSyncEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _savedAutoSyncEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private string _selectedIntervalLabel = OfflineSetting.AutoSyncIntervalOptions[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedIntervalLabel = OfflineSetting.AutoSyncIntervalOptions[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private string _selectedMaxAttemptsLabel = OfflineSetting.MaxAttemptOptions[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _savedMaxAttemptsLabel = OfflineSetting.MaxAttemptOptions[1];

    // ---- queue grid ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredQueue))]
    [NotifyPropertyChangedFor(nameof(QueueRowCount))]
    private ObservableCollection<PendingChange> _queue = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredQueue))]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredQueue))]
    private string _searchText = string.Empty;

    [ObservableProperty] private PendingChange? _selectedChange;

    [ObservableProperty] private string _successMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _warningsText = string.Empty;
    [ObservableProperty] private string _lastChangedByText = "not changed yet";
    [ObservableProperty] private bool _isBusy;

    public List<string> StatusFilters => _offlineQueue.StatusFilters;

    public List<string> IntervalOptions => OfflineSetting.AutoSyncIntervalOptions;

    public List<string> MaxAttemptOptions => OfflineSetting.MaxAttemptOptions;

    public string ConnectionText => IsOnline
        ? "Online — the HQ endpoint answers; queued changes can be replayed."
        : "Offline — changes stay in the local queue and keep the till running.";

    /// <summary>Drives the red banner; inverse of the probe so the view needs no converter trick.</summary>
    public bool IsOffline => !IsOnline;

    public bool IsDirty => IsForcedOffline != SavedIsForcedOffline
                           || AutoSyncEnabled != SavedAutoSyncEnabled
                           || !string.Equals(SelectedIntervalLabel, SavedIntervalLabel, StringComparison.Ordinal)
                           || !string.Equals(SelectedMaxAttemptsLabel, SavedMaxAttemptsLabel, StringComparison.Ordinal);

    /// <summary>Live reading of the unsaved switches, exactly as the queue would honour them.</summary>
    public string SyncSummary => OfflineSetting.Describe(BuildSetting());

    public int QueueRowCount => Queue.Count;

    public IEnumerable<PendingChange> FilteredQueue
    {
        get
        {
            var text = SearchText?.Trim();
            return Queue.Where(p => (SelectedStatusFilter == "All" || p.Status == SelectedStatusFilter)
                                   && (string.IsNullOrEmpty(text)
                                       || p.EntityKey.Contains(text, StringComparison.OrdinalIgnoreCase)
                                       || p.EntityType.Contains(text, StringComparison.OrdinalIgnoreCase)
                                       || (p.LastError != null && p.LastError.Contains(text, StringComparison.OrdinalIgnoreCase))));
        }
    }

    public string SelectedSummary => SelectedChange == null
        ? "Select a row to see the JSON the HQ endpoint receives."
        : $"{SelectedChange.EntityType} · {SelectedChange.Action} · {SelectedChange.EntityKey} — " +
          $"{SelectedChange.Status}, {SelectedChange.AttemptText}, {SelectedChange.StoreLabel}, " +
          $"created {SelectedChange.CreatedAt.ToLocalTime():dd-MMM-yyyy HH:mm:ss}, last attempt {SelectedChange.LastAttemptText}";

    /// <summary>Selected row's payload, pretty-printed for reading; shown raw when it is not valid JSON.</summary>
    public string SelectedPayloadJson
    {
        get
        {
            var raw = SelectedChange?.PayloadJson;
            if (string.IsNullOrWhiteSpace(raw))
                return "(no payload stored on this row)";

            try
            {
                using var doc = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(doc.RootElement, PrettyOpts);
            }
            catch (JsonException)
            {
                return raw;
            }
        }
    }

    public OfflineSyncViewModel(IOfflineQueueService offlineQueue) => _offlineQueue = offlineQueue;

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            // View-driven auto-sync: the service only sends when auto-sync is ON and the interval
            // has elapsed, so opening this screen never spams the endpoint.
            var autoSynced = await _offlineQueue.RunAutoSyncAsync();

            await LoadSettingsFormAsync();
            await ReloadBoardAsync();

            ErrorMessage = _offlineQueue.LastValidationError ?? string.Empty;
            SuccessMessage = autoSynced ? "Auto-sync delivered queued changes to HQ." : string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Loading the offline queue failed: {ex.Message}";
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
        var (success, error) = await _offlineQueue.SaveSettingsAsync(
            IsForcedOffline, AutoSyncEnabled, SelectedIntervalLabel, SelectedMaxAttemptsLabel);

        if (!success)
        {
            SuccessMessage = string.Empty;
            ErrorMessage = error ?? "Failed to save the offline sync settings.";
            return;
        }

        SavedIsForcedOffline = IsForcedOffline;
        SavedAutoSyncEnabled = AutoSyncEnabled;
        SavedIntervalLabel = SelectedIntervalLabel;
        SavedMaxAttemptsLabel = SelectedMaxAttemptsLabel;

        ErrorMessage = string.Empty;
        SuccessMessage = IsForcedOffline
            ? "Offline settings saved — forced offline is ON, so nothing will be sent."
            : "Offline settings saved.";

        await ReloadBoardAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync() => await RunAsync("Sync", () => _offlineQueue.FlushAsync("Manual"));

    [RelayCommand]
    private async Task RetryFailedAsync() => await RunAsync("Retry", () => _offlineQueue.RetryFailedAsync());

    private async Task RunAsync(string label, Func<Task<OfflineSyncOutcome>> action)
    {
        IsBusy = true;
        try
        {
            var outcome = await action();
            SuccessMessage = outcome.Summary;
            ErrorMessage = _offlineQueue.LastValidationError ?? string.Empty;
            await ReloadBoardAsync();
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

    [RelayCommand]
    private async Task DiscardSelectedAsync() => await DiscardCoreAsync(SelectedChange);

    [RelayCommand]
    private async Task DiscardRowAsync(PendingChange? change) => await DiscardCoreAsync(change);

    private async Task DiscardCoreAsync(PendingChange? change)
    {
        if (change == null)
        {
            SuccessMessage = string.Empty;
            ErrorMessage = "Select a queue row to discard, or use the row's own Discard button.";
            return;
        }

        IsBusy = true;
        try
        {
            var (success, error) = await _offlineQueue.DiscardAsync(change.Id,
                "Discarded from the Offline Sync screen");

            ErrorMessage = success ? string.Empty : error ?? "Discard failed.";
            SuccessMessage = success
                ? $"{change.RowLabel} discarded — it stays in the queue for history but will never be sent."
                : string.Empty;

            await ReloadBoardAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PurgeSyncedAsync()
    {
        IsBusy = true;
        try
        {
            var (success, purged, error) = await _offlineQueue.PurgeSyncedAsync();
            ErrorMessage = success ? string.Empty : error ?? "Purge failed.";
            SuccessMessage = success
                ? purged == 0
                    ? "No delivered rows to purge — the queue holds no Synced changes."
                    : $"Purged {purged} delivered row(s) from the queue."
                : string.Empty;
            await ReloadBoardAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Purge failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSettingsFormAsync()
    {
        var settings = await _offlineQueue.GetSettingsAsync();

        IsForcedOffline = settings.IsForcedOffline;
        SavedIsForcedOffline = settings.IsForcedOffline;
        AutoSyncEnabled = settings.AutoSyncEnabled;
        SavedAutoSyncEnabled = settings.AutoSyncEnabled;

        // Nearest vocabulary label, so the ComboBox always matches one of its items.
        SelectedIntervalLabel = OfflineSetting.IntervalLabel(settings.AutoSyncMinutes);
        SavedIntervalLabel = SelectedIntervalLabel;
        SelectedMaxAttemptsLabel = OfflineSetting.AttemptLabel(settings.MaxAttempts);
        SavedMaxAttemptsLabel = SelectedMaxAttemptsLabel;

        LastChangedByText = string.IsNullOrWhiteSpace(settings.UpdatedBy)
            ? "not changed yet"
            : $"{settings.UpdatedBy} on {settings.UpdatedAt.ToLocalTime():dd-MMM-yyyy HH:mm}";
    }

    /// <summary>Counts + grid + link state, without running an auto-sync pass.</summary>
    private async Task ReloadBoardAsync()
    {
        ApplyStats(await _offlineQueue.GetStatsAsync());

        var keepSelectedId = SelectedChange?.Id;
        Queue = new ObservableCollection<PendingChange>(await _offlineQueue.GetPageAsync(0, PageSize));
        if (keepSelectedId.HasValue)
            SelectedChange = Queue.FirstOrDefault(p => p.Id == keepSelectedId.Value);

        IsOnline = await _offlineQueue.IsOnlineAsync();

        var nextUp = await _offlineQueue.PeekAsync(3);
        NextUpText = nextUp.Count == 0
            ? "queue is empty"
            : string.Join(" · ", nextUp.Select(p => $"{p.EntityType} {p.EntityKey}"));

        WarningsText = _offlineQueue.Warnings.Count == 0
            ? string.Empty
            : "Note: " + string.Join(" | ", _offlineQueue.Warnings.Take(3));
    }

    private void ApplyStats(OfflineQueueStats stats)
    {
        TotalCount = stats.Total;
        QueuedCount = stats.Queued;
        SyncedCount = stats.Synced;
        FailedCount = stats.Failed;
        DiscardedCount = stats.Discarded;
        PendingCount = stats.PendingCount;

        LastSyncText = stats.LastSyncAt == null
            ? "never"
            : stats.LastSyncAt.Value.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");

        LastResultText = string.IsNullOrWhiteSpace(stats.LastResult)
            ? "No sync run recorded yet."
            : stats.LastResult;

        OldestPendingText = stats.OldestQueuedAt == null
            ? "nothing waiting"
            : $"oldest change waiting {stats.OldestPendingAge} (since {stats.OldestQueuedAt.Value.ToLocalTime():dd-MMM HH:mm})";

        AutoSyncText = stats.AutoSyncEnabled
            ? $"auto-sync {OfflineSetting.IntervalLabel(stats.AutoSyncMinutes).ToLowerInvariant()}, " +
              $"ceiling {Math.Max(1, stats.MaxAttempts)} attempt(s) per change"
            : "auto-sync off — replay only on demand";
    }

    private OfflineSetting BuildSetting() => new()
    {
        Id = 1,
        IsForcedOffline = IsForcedOffline,
        AutoSyncEnabled = AutoSyncEnabled,
        AutoSyncMinutes = OfflineSetting.MinutesForInterval(SelectedIntervalLabel),
        MaxAttempts = OfflineSetting.AttemptsFor(SelectedMaxAttemptsLabel),
    };

    partial void OnSelectedChangeChanged(PendingChange? value)
    {
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(SelectedPayloadJson));
    }

    private static readonly JsonSerializerOptions PrettyOpts = new() { WriteIndented = true };
}
