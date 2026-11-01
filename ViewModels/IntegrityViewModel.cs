using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-07: nightly/one-click data integrity check with orphan detection and
/// database optimisation (VACUUM + ANALYZE).
/// </summary>
public partial class IntegrityViewModel : BaseViewModel
{
    private readonly IIntegrityService _integrityService;

    [ObservableProperty]
    private ObservableCollection<IntegrityResultRow> _results = new();

    [ObservableProperty]
    private bool _isHealthy = true;

    [ObservableProperty]
    private DateTime _lastRun;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public string HealthBannerText => IsHealthy
        ? "Database looks healthy — no problems found."
        : "Issues were found — inspect the rows below and restore from a backup if the structure is corrupt.";

    public IntegrityViewModel(IIntegrityService integrityService)
    {
        _integrityService = integrityService;
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await _integrityService.RunIntegrityCheckAsync();
            Results = new ObservableCollection<IntegrityResultRow>(rows);
            IsHealthy = rows.All(r => r.Status != "Problem");
            LastRun = DateTime.Now;
            ErrorMessage = string.Empty;
            SuccessMessage = $"Integrity check completed at {LastRun:dd-MMM-yyyy HH:mm:ss}.";
            OnPropertyChanged(nameof(HealthBannerText));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Integrity check failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RunCheckAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task VacuumAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await _integrityService.VacuumAsync();
            foreach (var row in rows)
                Results.Add(row);
            ErrorMessage = string.Empty;
            SuccessMessage = "VACUUM and ANALYZE completed successfully.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"VACUUM failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
