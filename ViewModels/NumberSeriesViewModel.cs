using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class NumberSeriesViewModel : BaseViewModel
{
    private readonly INumberSeriesService _numberSeriesService;

    [ObservableProperty]
    private ObservableCollection<NumberSeries> _series = new();

    [ObservableProperty]
    private NumberSeries _selectedSeries = null!;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newPrefix = string.Empty;

    [ObservableProperty]
    private string _newSuffix = string.Empty;

    [ObservableProperty]
    private int _newStartNumber = 1;

    [ObservableProperty]
    private string _newFinancialYear = string.Empty;

    [ObservableProperty]
    private string _newDocType = string.Empty;

    [ObservableProperty]
    private bool _newResetEachYear = true;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // INV-03: gap audit + FY rollover
    [ObservableProperty]
    private ObservableCollection<NumberGapRow> _gapRows = new();

    [ObservableProperty]
    private bool _hasGapRows;

    [ObservableProperty]
    private string _rollOverFinancialYearText = string.Empty;

    private readonly HashSet<int> _dirtySeriesIds = new();

    public List<string> DocTypes { get; } = new()
    {
        "Invoice", "Purchase", "CreditNote", "Estimate"
    };

    public List<string> FinancialYears { get; } = new()
    {
        "2025-26", "2026-27", "2027-28"
    };

    public NumberSeriesViewModel(INumberSeriesService numberSeriesService)
    {
        _numberSeriesService = numberSeriesService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var seriesList = await _numberSeriesService.GetAllNumberSeriesAsync();
        Series = new ObservableCollection<NumberSeries>(seriesList);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddSeriesAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewPrefix) || string.IsNullOrWhiteSpace(NewFinancialYear) || string.IsNullOrWhiteSpace(NewDocType))
        {
            ErrorMessage = "All fields are required.";
            return;
        }

        // INV-03: guard against duplicate series for the same doc type + FY
        var duplicate = await _numberSeriesService.GetNumberSeriesAsync(NewDocType, NewFinancialYear);
        if (duplicate != null)
        {
            ErrorMessage = $"A series for {NewDocType} already exists in FY {NewFinancialYear} (\"{duplicate.Name}\"). Edit it or delete it first.";
            return;
        }

        var series = new NumberSeries
        {
            Name = NewName,
            Prefix = NewPrefix,
            Suffix = NewSuffix,
            StartNumber = NewStartNumber,
            CurrentNumber = NewStartNumber,
            FinancialYear = NewFinancialYear,
            DocType = NewDocType,
            ResetEachYear = NewResetEachYear,
            IsActive = true
        };

        var success = await _numberSeriesService.AddNumberSeriesAsync(series);
        if (success)
        {
            NewName = string.Empty;
            NewPrefix = string.Empty;
            NewSuffix = string.Empty;
            NewStartNumber = 1;
            NewDocType = string.Empty;
            NewResetEachYear = true;
            ErrorMessage = string.Empty;
            SuccessMessage = $"Series \"{series.Name}\" added - next number {series.NextNumber}.";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add number series.";
        }
    }

    [RelayCommand]
    private async Task DeleteSeriesAsync()
    {
        if (SelectedSeries == null) return;

        var success = await _numberSeriesService.DeleteNumberSeriesAsync(SelectedSeries.Id);
        if (success)
        {
            SelectedSeries = null!;
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task ResetSeriesAsync()
    {
        if (SelectedSeries == null) return;

        SelectedSeries.CurrentNumber = SelectedSeries.StartNumber;
        var success = await _numberSeriesService.UpdateNumberSeriesAsync(SelectedSeries);
        if (success)
        {
            SuccessMessage = $"Next number reset to {SelectedSeries.NextNumber}.";
            await LoadDataAsync();
        }
    }

    /// <summary>INV-03: called from the view when a row's Prefix/Suffix cells are edited inline.</summary>
    public void MarkDirty(NumberSeries? series)
    {
        if (series != null) _dirtySeriesIds.Add(series.Id);
    }

    /// <summary>INV-03: persist inline edits to prefixes/suffixes.</summary>
    [RelayCommand]
    private async Task SaveEditsAsync()
    {
        if (_dirtySeriesIds.Count == 0)
        {
            SuccessMessage = "Nothing changed.";
            return;
        }

        var saved = 0;
        foreach (var id in _dirtySeriesIds)
        {
            var series = Series.FirstOrDefault(x => x.Id == id);
            if (series == null) continue;
            if (string.IsNullOrWhiteSpace(series.Prefix))
            {
                ErrorMessage = $"Series \"{series.Name}\" needs a prefix.";
                return;
            }
            if (await _numberSeriesService.UpdateNumberSeriesAsync(series))
                saved++;
        }

        _dirtySeriesIds.Clear();
        SuccessMessage = $"{saved} series updated - new numbers will use the edited prefix/suffix.";
        ErrorMessage = string.Empty;
        await LoadDataAsync();
    }

    /// <summary>INV-03: audit issued numbers for gaps and duplicates.</summary>
    [RelayCommand]
    private async Task CheckGapsAsync()
    {
        try
        {
            var rows = await _numberSeriesService.GetGapReportAsync();
            GapRows = new ObservableCollection<NumberGapRow>(rows);
            HasGapRows = rows.Count > 0;
            var problems = rows.Count(r => r.Status == "Gaps/Duplicates");
            SuccessMessage = problems == 0
                ? "Gap audit complete - all series are contiguous."
                : $"{problems} series with gaps/duplicates found - see the Gap Audit table.";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>INV-03: clone all ResetEachYear series into a new financial year.</summary>
    [RelayCommand]
    private async Task RollOverYearAsync()
    {
        var fy = RollOverFinancialYearText?.Trim();
        if (string.IsNullOrWhiteSpace(fy))
        {
            ErrorMessage = "Enter the new financial year, e.g. 2027-28.";
            return;
        }

        try
        {
            var created = await _numberSeriesService.RollOverFinancialYearAsync(fy);
            SuccessMessage = created > 0
                ? $"{created} series rolled over into FY {fy}, restarting at their start numbers."
                : $"Nothing to roll over - FY {fy} series already exist or no repeating series found.";
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
