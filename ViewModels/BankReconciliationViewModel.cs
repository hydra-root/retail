using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-06: bank reconciliation - import statement CSV, auto-match, manual reconcile.
/// </summary>
public partial class BankReconciliationViewModel : BaseViewModel
{
    private readonly IBankReconciliationService _reconciliationService;

    [ObservableProperty]
    private ObservableCollection<BankStatementLine> _rows = new();

    [ObservableProperty]
    private BankStatementLine? _selectedLine;

    [ObservableProperty]
    private string _statusFilter = "Unmatched";

    [ObservableProperty]
    private BankReconciliationSummary _summary = new();

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Filters { get; } = new() { "All", "Unmatched", "Reconciled" };

    public BankReconciliationViewModel(IBankReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync() => await RefreshLinesAsync();

    private async System.Threading.Tasks.Task RefreshLinesAsync()
    {
        IsLoading = true;
        try
        {
            var lines = await _reconciliationService.GetLinesAsync(StatusFilter);
            Rows = new ObservableCollection<BankStatementLine>(lines);
            Summary = await _reconciliationService.GetSummaryAsync();
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnStatusFilterChanged(string value) => _ = RefreshLinesAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshLinesAsync();

    /// <summary>ACC-06: pick a bank statement CSV and import it.</summary>
    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import bank statement CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            var (imported, warnings) = await _reconciliationService.ImportCsvAsync(dialog.FileName);
            SuccessMessage = $"Imported {imported} rows from {Path.GetFileName(dialog.FileName)}.";
            if (warnings.Count > 0)
                SuccessMessage += $" {warnings.Count} row(s) skipped.";
            ErrorMessage = string.Empty;
            await RefreshLinesAsync();

            // surface the first few skipped-row warnings
            if (warnings.Count > 0)
                ErrorMessage = string.Join(" ", warnings.Take(3));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>ACC-06: auto-match unmatched lines against Cash/Bank journal lines.</summary>
    [RelayCommand]
    private async Task AutoMatchAsync()
    {
        try
        {
            var matched = await _reconciliationService.AutoMatchAsync();
            SuccessMessage = matched > 0
                ? $"Auto-matched {matched} statement line(s) against journal entries."
                : "No new matches found - check dates/amounts or reconcile manually.";
            ErrorMessage = string.Empty;
            await RefreshLinesAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>ACC-06: manually mark the selected line reconciled.</summary>
    [RelayCommand]
    private async Task ReconcileAsync()
    {
        if (SelectedLine == null)
        {
            ErrorMessage = "Select a statement line first.";
            return;
        }

        var ok = await _reconciliationService.MarkReconciledAsync(SelectedLine.Id);
        if (ok)
        {
            SuccessMessage = "Line reconciled manually.";
            ErrorMessage = string.Empty;
            await RefreshLinesAsync();
        }
    }

    /// <summary>ACC-06: clear the match on the selected line.</summary>
    [RelayCommand]
    private async Task ClearMatchAsync()
    {
        if (SelectedLine == null)
        {
            ErrorMessage = "Select a statement line first.";
            return;
        }

        var ok = await _reconciliationService.ClearMatchAsync(SelectedLine.Id);
        if (ok)
        {
            SuccessMessage = "Match cleared.";
            ErrorMessage = string.Empty;
            await RefreshLinesAsync();
        }
    }
}
