using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-01: editor for the automatic backup schedule — settings with an IsDirty-gated Save,
/// a live preview of the resolved schedule and next run, scheduler start/stop, a run-now
/// trigger and the backup history grid (with scheduler-produced rows marked).
/// </summary>
public partial class BackupScheduleViewModel : BaseViewModel
{
    private readonly IBackupSchedulerService _scheduler;
    private readonly IBackupService _backupService;

    /// <summary>The persisted row; carries Id and LastRunAt so previews anchor correctly.</summary>
    private BackupScheduleSetting _loaded = new();

    private bool _loading;
    private string _savedSignature = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedFrequency = BackupScheduleSetting.FrequencyDaily;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _intervalDays = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _selectedDayOfWeek = "Sunday";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _runAtHour = 21;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _runAtMinute = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _destinationFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _keepGenerations = 14;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _includeManualBackups;

    [ObservableProperty]
    private string _scheduleSummary = string.Empty;

    [ObservableProperty]
    private string _nextRunPreview = string.Empty;

    [ObservableProperty]
    private string _stateText = "Disabled";

    [ObservableProperty]
    private string _lastRunText = "never";

    [ObservableProperty]
    private string _lastResultText = "no run recorded yet";

    [ObservableProperty]
    private string _nextRunText = "not scheduled";

    [ObservableProperty]
    private string _countdownText = string.Empty;

    [ObservableProperty]
    private bool _schedulerRunning;

    [ObservableProperty]
    private ObservableCollection<BackupRow> _backups = new();

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Frequencies => BackupScheduleSetting.FrequencyOptions;
    public List<string> DaysOfWeek => BackupScheduleSetting.DayOfWeekOptions;

    public bool IsDirty => EditSignature() != _savedSignature;

    public BackupScheduleViewModel(IBackupSchedulerService scheduler, IBackupService backupService)
    {
        _scheduler = scheduler;
        _backupService = backupService;
    }

    public async Task LoadDataAsync()
    {
        _loading = true;
        IsLoading = true;
        try
        {
            var settings = await _scheduler.GetSettingsAsync();
            _loaded = settings;

            IsEnabled = settings.IsEnabled;
            SelectedFrequency = settings.Frequency;
            IntervalDays = settings.IntervalDays;
            SelectedDayOfWeek = settings.DayOfWeek;
            RunAtHour = settings.RunAtHour;
            RunAtMinute = settings.RunAtMinute;
            DestinationFolder = settings.DestinationFolder ?? string.Empty;
            KeepGenerations = settings.KeepGenerations;
            IncludeManualBackups = settings.IncludeManualBackupsInPrune;
            _savedSignature = EditSignature();

            RefreshStatusCard(settings);

            SuccessMessage = string.Empty;
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load the schedule: {ex.Message}";
        }
        finally
        {
            _loading = false;
            IsLoading = false;
        }

        UpdatePreviews();
        await LoadHistoryAsync();
    }

    private void RefreshStatusCard(BackupScheduleSetting s)
    {
        SchedulerRunning = _scheduler.IsRunning;
        StateText = s.IsEnabled
            ? (_scheduler.IsRunning ? "Enabled · running" : "Enabled · stopped (Start to arm)")
            : "Disabled";

        LastRunText = s.LastRunAt is { } lr ? lr.ToString("ddd dd-MMM-yyyy HH:mm") : "never";
        LastResultText = string.IsNullOrWhiteSpace(s.LastResult) ? "no run recorded yet" : s.LastResult;

        if (s.NextRunAt is { } nr)
        {
            NextRunText = nr.ToString("ddd dd-MMM-yyyy HH:mm");
            CountdownText = s.IsEnabled && nr > DateTime.Now
                ? $"next run in {DescribeInterval(DateTime.Now, nr)}"
                : (s.IsEnabled ? "due now — will run on the next tick" : "schedule is disabled");
        }
        else
        {
            NextRunText = "not scheduled";
            CountdownText = s.IsEnabled ? "not scheduled — save to set the next run" : "schedule is disabled";
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var backups = await _backupService.ListBackupsAsync();
            Backups = new ObservableCollection<BackupRow>(backups.Select(b => new BackupRow
            {
                FileName = b.FileName,
                CreatedAt = b.CreatedAt,
                SizeText = $"{b.SizeBytes / 1024.0:F1} KB",
                IsFromScheduler = b.FileName.StartsWith(
                    BackupScheduleSetting.AutoFilePrefix, StringComparison.Ordinal),
            }));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to list backups: {ex.Message}";
        }
    }

    // ---- preview helpers ----

    private BackupScheduleSetting BuildDraft() => new()
    {
        Id = _loaded.Id == 0 ? 1 : _loaded.Id,
        IsEnabled = IsEnabled,
        Frequency = SelectedFrequency,
        IntervalDays = IntervalDays,
        DayOfWeek = SelectedDayOfWeek,
        RunAtHour = RunAtHour,
        RunAtMinute = RunAtMinute,
        DestinationFolder = string.IsNullOrWhiteSpace(DestinationFolder) ? null : DestinationFolder.Trim(),
        KeepGenerations = KeepGenerations,
        IncludeManualBackupsInPrune = IncludeManualBackups,
        LastRunAt = _loaded.LastRunAt,
    };

    private string EditSignature() => string.Join('|',
        IsEnabled, SelectedFrequency, IntervalDays, SelectedDayOfWeek, RunAtHour, RunAtMinute,
        (DestinationFolder ?? string.Empty).Trim(), KeepGenerations, IncludeManualBackups);

    private void UpdatePreviews()
    {
        var draft = BuildDraft();
        ScheduleSummary = draft.ScheduleSummary;

        var next = _scheduler.ComputeNextRun(draft, DateTime.Now);
        NextRunPreview = next is { } n
            ? $"Next scheduled run: {n:ddd dd-MMM-yyyy HH:mm}  (in {DescribeInterval(DateTime.Now, n)})"
            : "No upcoming run for this schedule.";
    }

    private static string DescribeInterval(DateTime from, DateTime to)
    {
        var span = to - from;
        if (span.TotalMinutes < 1) return "less than a minute";

        var days = (int)span.TotalDays;
        var parts = new List<string>();
        if (days > 0) parts.Add($"{days} day{(days == 1 ? "" : "s")}");
        if (span.Hours > 0) parts.Add($"{span.Hours} hour{(span.Hours == 1 ? "" : "s")}");
        if (days == 0 && span.Minutes > 0) parts.Add($"{span.Minutes} minute{(span.Minutes == 1 ? "" : "s")}");
        return string.Join(" ", parts);
    }

    // recompute the live preview whenever an editable field changes (but not while loading)
    partial void OnIsEnabledChanged(bool value) { if (!_loading) UpdatePreviews(); }
    partial void OnSelectedFrequencyChanged(string value) { if (!_loading) UpdatePreviews(); }
    partial void OnIntervalDaysChanged(int value) { if (!_loading) UpdatePreviews(); }
    partial void OnSelectedDayOfWeekChanged(string value) { if (!_loading) UpdatePreviews(); }
    partial void OnRunAtHourChanged(int value) { if (!_loading) UpdatePreviews(); }
    partial void OnRunAtMinuteChanged(int value) { if (!_loading) UpdatePreviews(); }
    partial void OnDestinationFolderChanged(string value) { if (!_loading) UpdatePreviews(); }
    partial void OnKeepGenerationsChanged(int value) { if (!_loading) UpdatePreviews(); }
    partial void OnIncludeManualBackupsChanged(bool value) { if (!_loading) UpdatePreviews(); }

    // ---- commands ----

    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        var (ok, error) = await _scheduler.SaveSettingsAsync(BuildDraft());
        if (ok)
            SuccessMessage = "Backup schedule saved.";
        else
            ErrorMessage = error ?? "Failed to save the backup schedule.";

        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        var (ok, message, path) = await _scheduler.RunNowAsync();
        if (ok)
            SuccessMessage = path is null ? message : $"{message} Saved to: {path}";
        else
            ErrorMessage = message;

        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task StartSchedulerAsync()
    {
        _scheduler.Start();
        SuccessMessage = "Scheduler started — backups will run on schedule while the app is open.";
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task StopSchedulerAsync()
    {
        _scheduler.Stop();
        SuccessMessage = "Scheduler stopped — scheduled backups are paused until started again.";
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();
}

/// <summary>History row: a backup file plus whether this scheduler produced it.</summary>
public sealed class BackupRow
{
    public string FileName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string SizeText { get; init; } = string.Empty;
    public bool IsFromScheduler { get; init; }
    public string Source => IsFromScheduler ? "Automatic" : "Manual";
}
