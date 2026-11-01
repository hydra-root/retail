using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-12: bulk CSV import/export of Products and Customers - validation preview,
/// one-click commit, template download and full CSV export.
/// </summary>
public partial class BulkImportViewModel : BaseViewModel
{
    private readonly IBulkImportService _bulkImportService;
    private string? _selectedFilePath;

    [ObservableProperty]
    private string _selectedEntityType = "Products";

    [ObservableProperty]
    private ObservableCollection<BulkImportRow> _previewRows = new();

    [ObservableProperty]
    private int _totalImported;

    [ObservableProperty]
    private int _totalFailed;

    [ObservableProperty]
    private string _selectedFile = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True once a CSV has been previewed; enables Commit.</summary>
    [ObservableProperty]
    private bool _hasPreview;

    public List<string> EntityType { get; } = new() { "Products", "Customers" };

    public BulkImportViewModel(IBulkImportService bulkImportService)
    {
        _bulkImportService = bulkImportService;
    }

    /// <summary>Called by the view's Loaded hook; nothing to reload, messages persist.</summary>
    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>Switching Products/Customers resets the previous file preview.</summary>
    partial void OnSelectedEntityTypeChanged(string value)
    {
        _selectedFilePath = null;
        SelectedFile = string.Empty;
        HasPreview = false;
        PreviewRows.Clear();
        TotalImported = TotalFailed = 0;
        ErrorMessage = SuccessMessage = string.Empty;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var path = PickCsvFile();
        if (path == null) return;

        IsLoading = true;
        try
        {
            _selectedFilePath = path;
            SelectedFile = path;
            var result = await _bulkImportService.ValidateCsvAsync(SelectedEntityType, path);

            PreviewRows = new ObservableCollection<BulkImportRow>(result.SampleRows ?? Enumerable.Empty<BulkImportRow>());
            TotalImported = result.Imported;
            TotalFailed = result.Failed;
            HasPreview = true;
            ErrorMessage = string.Join(Environment.NewLine, result.Errors);
            SuccessMessage = result.Failed == 0
                ? $"Found {result.Imported} valid {SelectedEntityType} rows. Review the preview, then Commit."
                : $"Preview complete: {result.Imported} valid, {result.Failed} failed. Fix the errors below and re-select the file to retry.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
            SuccessMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (!HasPreview || string.IsNullOrEmpty(_selectedFilePath))
        {
            ErrorMessage = "Browse & Preview a CSV file first.";
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _bulkImportService.CommitAsync(SelectedEntityType, _selectedFilePath);

            TotalImported = result.Imported;
            TotalFailed = result.Failed;
            ErrorMessage = string.Join(Environment.NewLine, result.Errors);
            SuccessMessage = result.Imported > 0
                ? $"Imported {result.Imported} {SelectedEntityType} ({result.Failed} failed)."
                : $"Nothing imported - {result.Failed} row(s) failed validation.";
            HasPreview = false; // force a fresh preview before another commit
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            SuccessMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportTemplate()
    {
        try
        {
            var path = await _bulkImportService.ExportTemplateCsvAsync(SelectedEntityType);
            SuccessMessage = $"Template saved to {path}";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportAll()
    {
        IsLoading = true;
        try
        {
            var path = await _bulkImportService.ExportAllCsvAsync(SelectedEntityType);
            SuccessMessage = $"Exported all {SelectedEntityType} to {path}";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>OpenFile picker; ShowDialog in the VM is allowed in this project.</summary>
    private string? PickCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Select a {SelectedEntityType} CSV file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
