using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-10 Audit trail: who changed what with before/after values.
/// Searchable list + a detail pane with the parsed changes diff.
/// </summary>
public partial class AuditTrailViewModel : BaseViewModel
{
    private readonly IAuditService _auditService;

    [ObservableProperty]
    private ObservableCollection<AuditRecord> _records = new();

    [ObservableProperty]
    private AuditRecord? _selectedRecord;

    [ObservableProperty]
    private string _searchUsername = string.Empty;

    [ObservableProperty]
    private string _searchAction = string.Empty;

    [ObservableProperty]
    private string _searchEntity = string.Empty;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public AuditTrailViewModel(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var list = await _auditService.QueryAsync(
            username: NullIfEmpty(SearchUsername),
            action: NullIfEmpty(SearchAction),
            entityName: NullIfEmpty(SearchEntity),
            from: StartDate,
            to: EndDate);
        Records = new ObservableCollection<AuditRecord>(list);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"AuditTrail_{DateTime.Today:yyyyMMdd}.csv");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Timestamp,Username,Action,Entity,EntityId,Label,Changes");
            foreach (var r in Records)
                sb.AppendLine($"\"{r.Timestamp:dd-MMM-yyyy HH:mm:ss}\",{r.Username},{r.Action},{r.EntityName},{r.EntityId},\"{r.EntityLabel}\",\"{(r.ChangesJson ?? string.Empty).Replace("\"", "\"\"")}\"");
            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Parsed change entries for the detail pane (Property / Before / After).</summary>
    public IReadOnlyList<AuditChangeEntry> SelectedChanges
    {
        get
        {
            var list = new List<AuditChangeEntry>();
            var json = SelectedRecord?.ChangesJson;
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                return list;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string? before = null, after = null;
                    if (prop.Value.TryGetProperty("before", out var b))
                        before = DisplayValue(b);
                    if (prop.Value.TryGetProperty("after", out var a))
                        after = DisplayValue(a);
                    list.Add(new AuditChangeEntry
                    {
                        Property = prop.Name,
                        Before = before ?? "(null)",
                        After = after ?? "(null)"
                    });
                }
            }
            catch { /* malformed json on legacy rows */ }

            return list;
        }
    }

    public string SelectedSummary =>
        SelectedRecord == null
            ? string.Empty
            : $"{SelectedRecord.Action} — {SelectedRecord.EntityName} #{SelectedRecord.EntityId} ({SelectedRecord.EntityLabel}) by {SelectedRecord.Username} at {SelectedRecord.Timestamp:dd-MMM-yyyy HH:mm:ss}";

    partial void OnSelectedRecordChanged(AuditRecord? value)
    {
        OnPropertyChanged(nameof(SelectedChanges));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    private static string DisplayValue(System.Text.Json.JsonElement el) =>
        el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null => "(null)",
            System.Text.Json.JsonValueKind.String => el.GetString() ?? string.Empty,
            _ => el.ToString()
        };

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}

public class AuditChangeEntry
{
    public string Property { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}
