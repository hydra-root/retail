using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-08: reminder rule editor plus the candidate queue the rules produce, with the
/// send actions routed through the Messaging Center.
/// </summary>
public partial class ReminderViewModel : BaseViewModel
{
    private readonly IReminderService _reminderService;

    // --- rule editor form ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private int _firstNoticeDaysBeforeDue = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private int _secondNoticeDaysAfterDue = 7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private string _selectedChannel = "WhatsApp";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewMessage))]
    private string _messageTemplate = ReminderSetting.DefaultTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private int _maxPerCustomerPerDay = 1;

    // --- automatic run (armed from the backup scheduler's tick) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private bool _isAutoRunEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private int _autoRunHour = 9;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleSummary))]
    private int _autoRunMinute;

    [ObservableProperty]
    private string _lastAutoRunText = "never";

    // --- queue ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCandidates))]
    [NotifyPropertyChangedFor(nameof(QueuedCount))]
    [NotifyPropertyChangedFor(nameof(PreviewMessage))]
    private ObservableCollection<ReminderCandidateRow> _candidates = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCandidates))]
    private string _selectedFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCandidates))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _lastRunText = "never";

    [ObservableProperty]
    private bool _isBusy;

    public List<string> Channels => _reminderService.Channels;

    public List<string> Filters { get; } = new() { "All", "Upcoming", "Overdue", "Second Notice" };

    public string PlaceholderHint =>
        "Placeholders: {Customer} {InvoiceNumber} {Amount} {DueDate} {Days} {Company}";

    /// <summary>Hour (0-23) / minute (0-59) choices for the automatic run selectors.</summary>
    public List<int> HourOptions { get; } = Enumerable.Range(0, 24).ToList();

    public List<int> MinuteOptions { get; } = Enumerable.Range(0, 60).ToList();

    public int QueuedCount => Candidates.Count(c => c.Candidate.Included);

    /// <summary>Live reading of the unsaved form, so the operator sees the rule they are writing.</summary>
    public string RuleSummary => ReminderSetting.Describe(BuildSetting());

    public IEnumerable<ReminderCandidateRow> FilteredCandidates =>
        Candidates.Where(c => (SelectedFilter == "All" || c.Candidate.Stage == SelectedFilter)
                              && (string.IsNullOrWhiteSpace(SearchText)
                                  || c.CustomerName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
                                  || c.InvoiceNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>Template preview rendered with the edited (unsaved) template over the first queued row.</summary>
    public string PreviewMessage
    {
        get
        {
            var sample = Candidates.FirstOrDefault(c => c.Candidate.Included)
                         ?? Candidates.FirstOrDefault()
                         ?? new ReminderCandidateRow(SampleCandidate());
            return _reminderService.RenderTemplate(BuildSetting(), sample.Candidate);
        }
    }

    public ReminderViewModel(IReminderService reminderService) => _reminderService = reminderService;

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _reminderService.GetSettingsAsync();
            IsEnabled = settings.IsEnabled;
            FirstNoticeDaysBeforeDue = settings.FirstNoticeDaysBeforeDue;
            SecondNoticeDaysAfterDue = settings.SecondNoticeDaysAfterDue;
            SelectedChannel = settings.Channel;
            MessageTemplate = settings.MessageTemplate;
            MaxPerCustomerPerDay = settings.MaxPerCustomerPerDay;
            IsAutoRunEnabled = settings.AutoRunEnabled;
            AutoRunHour = Math.Clamp(settings.AutoRunHour, 0, 23);
            AutoRunMinute = Math.Clamp(settings.AutoRunMinute, 0, 59);
            LastRunText = FormatLastRun(settings.LastRunAt);
            LastAutoRunText = FormatAutoRun(settings.LastAutoRunAt, settings.LastAutoResult);

            await ReloadCandidatesAsync();

            SuccessMessage = string.Empty;
            ErrorMessage = _reminderService.LastValidationError ?? string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Loading reminders failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Refresh() => await LoadDataAsync();

    [RelayCommand]
    private async Task SaveSettings()
    {
        var (success, error) = await _reminderService.SaveSettingsAsync(BuildSetting());
        if (!success)
        {
            ErrorMessage = error ?? "Failed to save the reminder settings.";
            SuccessMessage = string.Empty;
            return;
        }

        ErrorMessage = string.Empty;
        SuccessMessage = "Reminder rules saved.";
        await ReloadCandidatesAsync();
        var settings = await _reminderService.GetSettingsAsync();
        LastRunText = FormatLastRun(settings.LastRunAt);
        LastAutoRunText = FormatAutoRun(settings.LastAutoRunAt, settings.LastAutoResult);
    }

    [RelayCommand]
    private async Task SendSelected() =>
        await RunSendAsync(Candidates.Where(c => c.IsSelected).Select(c => c.Candidate).ToList());

    [RelayCommand]
    private async Task SendAllQueued() =>
        await RunSendAsync(Candidates.Where(c => c.Candidate.Included).Select(c => c.Candidate).ToList());

    private async Task RunSendAsync(List<ReminderCandidate> selected)
    {
        SuccessMessage = string.Empty;
        if (selected.Count == 0)
        {
            ErrorMessage = "Nothing to send — tick at least one queued row (rows outside the notice window " +
                           "or without a channel address cannot be sent).";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _reminderService.SendAsync(selected);
            ErrorMessage = result.Failed > 0 ? string.Join(" | ", _reminderService.Errors) : string.Empty;
            SuccessMessage = $"Reminders: {result.Summary} {DetailsOf(result)}".Trim();

            await ReloadCandidatesAsync();
            var settings = await _reminderService.GetSettingsAsync();
            LastRunText = FormatLastRun(settings.LastRunAt);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sending reminders failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadCandidatesAsync()
    {
        var candidates = await _reminderService.GetCandidatesAsync(DateTime.Today);
        Candidates = new ObservableCollection<ReminderCandidateRow>(candidates.Select(c => new ReminderCandidateRow(c)));
    }

    /// <summary>First blocked row's reason, so the summary line explains a partial run without a second grid.</summary>
    private static string DetailsOf(ReminderSendResult result)
    {
        var first = result.Rows.FirstOrDefault(r => r.Outcome != "Sent");
        return first == null
            ? "All queued notices handed to the Messaging Center."
            : $"First skipped/failed row: {first.InvoiceNumber} — {first.Detail}";
    }

    private ReminderSetting BuildSetting() => new()
    {
        Id = 1,
        IsEnabled = IsEnabled,
        FirstNoticeDaysBeforeDue = FirstNoticeDaysBeforeDue,
        SecondNoticeDaysAfterDue = SecondNoticeDaysAfterDue,
        Channel = SelectedChannel,
        MessageTemplate = MessageTemplate,
        MaxPerCustomerPerDay = MaxPerCustomerPerDay,
        AutoRunEnabled = IsAutoRunEnabled,
        AutoRunHour = AutoRunHour,
        AutoRunMinute = AutoRunMinute,
    };

    private static string FormatLastRun(DateTime? lastRunAt) =>
        lastRunAt == null ? "never" : lastRunAt.Value.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");

    private static string FormatAutoRun(DateTime? lastAutoRunAt, string? lastAutoResult)
    {
        if (lastAutoRunAt == null)
            return "no automatic run yet";
        return $"{lastAutoRunAt.Value.ToLocalTime():dd-MMM-yyyy HH:mm} — {lastAutoResult ?? "(no result recorded)"}";
    }

    private static ReminderCandidate SampleCandidate() => new()
    {
        InvoiceId = 0,
        InvoiceNumber = "INV-0001",
        InvoiceDate = DateTime.Today.AddDays(-27),
        DueDate = DateTime.Today.AddDays(3),
        CustomerName = "Sharma Kirana Store",
        Recipient = "9820000000",
        Outstanding = 12345.50m,
        DaysToOrFromDue = 3,
        Stage = ReminderStages.Upcoming,
        Included = true,
    };
}

/// <summary>
/// PAY-08: selection wrapper around the ReminderCandidate DTO — the DTO stays plain,
/// the grid needs an INotifyPropertyChanged row for its checkboxes.
/// </summary>
public partial class ReminderCandidateRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ReminderCandidateRow(ReminderCandidate candidate) => Candidate = candidate;

    public ReminderCandidate Candidate { get; }

    public string InvoiceNumber => Candidate.InvoiceNumber;
    public string CustomerName => Candidate.CustomerName;
    public string StageLabel => Candidate.StageLabel;
    public decimal Outstanding => Candidate.Outstanding;
    public string Recipient => Candidate.Recipient;
    public string Message => Candidate.Message;
    public string Reason => Candidate.Reason;
    public string DaysText => Candidate.DaysLabel;
    public string DueDateText => Candidate.DueDate.ToString("dd-MMM-yyyy");

    /// <summary>Only rows the rules actually queued can be ticked for sending.</summary>
    public bool CanSelect => Candidate.Included;
}
