using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-06: Tally / Excel export screen. The warnings grid is the point of this
/// screen - TallyPrime rejects or mis-books quiet data, so every skip and every
/// mapping fallback has to be visible before the accountant touches Tally.
/// </summary>
public partial class TallyExportViewModel : BaseViewModel
{
    private readonly ITallyExportService _exportService;
    private readonly IStoreService _storeService;
    private readonly IFinancialYearService _financialYearService;

    /// <summary>Set once InitializeAsync completes so a re-attached view does not reset the user's dates.</summary>
    public bool IsInitialized { get; private set; }

    public class StoreOption
    {
        public int? StoreId { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    [ObservableProperty]
    private ObservableCollection<StoreOption> _stores = new();

    [ObservableProperty]
    private StoreOption? _selectedStore;

    [ObservableProperty]
    private string _selectedDocumentType = "Sales";

    [ObservableProperty]
    private string _selectedFormat = "Tally XML";

    [ObservableProperty]
    private DateTime _startDate = new(DateTime.Today.Year, 4, 1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _previewDocumentCount;

    [ObservableProperty]
    private int _previewSkippedCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastExportPath;

    [ObservableProperty]
    private ObservableCollection<string> _warnings = new();

    public List<string> DocumentTypes { get; } = new()
    {
        "Sales", "Purchases", "Journal", "Payments & Receipts", "Masters", "Combined"
    };

    public List<string> Formats { get; } = new() { "Tally XML", "CSV", "Excel XML (Day Book)" };

    public TallyExportViewModel(ITallyExportService exportService, IStoreService storeService,
        IFinancialYearService financialYearService)
    {
        _exportService = exportService;
        _storeService = storeService;
        _financialYearService = financialYearService;
    }

    private TallyDocumentType CurrentDocType => SelectedDocumentType switch
    {
        "Purchases" => TallyDocumentType.Purchases,
        "Journal" => TallyDocumentType.Journal,
        "Payments & Receipts" => TallyDocumentType.PaymentsReceipts,
        "Masters" => TallyDocumentType.Masters,
        "Combined" => TallyDocumentType.Combined,
        _ => TallyDocumentType.Sales,
    };

    private TallyExportFormat CurrentFormat => SelectedFormat == "Tally XML"
        ? TallyExportFormat.TallyXml
        : TallyExportFormat.Csv;

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            var stores = await _storeService.GetAllStoresAsync();
            Stores = new ObservableCollection<StoreOption>(
                new[] { new StoreOption { StoreId = null, Name = "All stores" } }
                    .Concat(stores.Select(s => new StoreOption { StoreId = s.Id, Name = s.Name })));

            // Default to the current financial year (1 April); fall back on the FY table
            // when one is registered, then on plain calendar arithmetic.
            var fy = await _financialYearService.GetCurrentFinancialYearAsync();
            StartDate = fy != null ? fy.StartDate.Date : DefaultFinancialYearStart(DateTime.Today);
            EndDate = DateTime.Today;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not initialise the export screen: {ex.Message}";
        }
        finally
        {
            IsInitialized = true;
        }
        await UpdatePreviewAsync();
    }

    internal static DateTime DefaultFinancialYearStart(DateTime today) =>
        today >= new DateTime(today.Year, 4, 1) ? new DateTime(today.Year, 4, 1)
                                                 : new DateTime(today.Year - 1, 4, 1);

    partial void OnStartDateChanged(DateTime value) => _ = UpdatePreviewAsync();
    partial void OnEndDateChanged(DateTime value) => _ = UpdatePreviewAsync();

    partial void OnSelectedDocumentTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsStoreFilterVisible));
        _ = UpdatePreviewAsync();
    }

    partial void OnSelectedStoreChanged(StoreOption? value) => _ = UpdatePreviewAsync();

    /// <summary>Store filter only narrows sales/payments; purchases and journals carry no store.</summary>
    public bool IsStoreFilterVisible => SelectedDocumentType is "Sales" or "Payments & Receipts" or "Combined";

    private async Task UpdatePreviewAsync()
    {
        if (!IsInitialized || IsBusy) return;
        try
        {
            var preview = await _exportService.PreviewAsync(CurrentDocType, CurrentFormat,
                StartDate, EndDate, SelectedStore?.StoreId);
            if (preview.Error != null)
            {
                PreviewDocumentCount = 0;
                PreviewSkippedCount = 0;
                ErrorMessage = preview.Error;
                return;
            }
            ErrorMessage = string.Empty;
            PreviewDocumentCount = preview.DocumentCount;
            PreviewSkippedCount = preview.SkippedCount;
            Warnings = new ObservableCollection<string>(preview.Warnings);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (StartDate > EndDate)
        {
            ErrorMessage = "Start date must be on or before end date.";
            return;
        }
        if (SelectedDocumentType == "Masters" && SelectedFormat != "Tally XML")
        {
            ErrorMessage = "Masters are only exported as Tally XML - vouchers cannot resolve without them.";
            return;
        }

        IsBusy = true;
        SuccessMessage = string.Empty;
        ErrorMessage = string.Empty;
        try
        {
            string? dir = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder;
            int? storeId = IsStoreFilterVisible ? SelectedStore?.StoreId : null;
            var (from, to) = (StartDate, EndDate);

            var result = (SelectedDocumentType, SelectedFormat) switch
            {
                ("Sales", "Tally XML") => await _exportService.ExportSalesXmlAsync(from, to, storeId, dir),
                ("Purchases", "Tally XML") => await _exportService.ExportPurchaseXmlAsync(from, to, dir),
                ("Journal", "Tally XML") => await _exportService.ExportJournalXmlAsync(from, to, dir),
                ("Payments & Receipts", "Tally XML") => await _exportService.ExportPaymentReceiptXmlAsync(from, to, storeId, dir),
                ("Masters", _) => await _exportService.ExportMastersXmlAsync(dir),
                ("Combined", "Tally XML") => await _exportService.ExportCombinedXmlAsync(from, to, storeId, dir),
                ("Sales", "CSV") => await _exportService.ExportSalesCsvAsync(from, to, storeId, dir),
                ("Purchases", "CSV") => await _exportService.ExportPurchaseCsvAsync(from, to, dir),
                (_, "Excel XML (Day Book)") => await _exportService.ExportDayBookSpreadsheetXmlAsync(from, to, storeId, dir),
                _ => await _exportService.ExportDayBookCsvAsync(from, to, storeId, dir),
            };

            if (SelectedFormat != "Tally XML" && SelectedDocumentType is "Journal" or "Payments & Receipts" or "Combined")
                result.Warnings.Add($"This document type has no dedicated {SelectedFormat} layout - exported " +
                    $"the combined Day Book instead (skips shown: {result.SkippedCount}).");

            Warnings = new ObservableCollection<string>(result.Warnings);
            LastExportPath = result.Success ? result.FilePath : null;

            if (result.Success)
            {
                var xmlNote = result.XmlElementCount > 0 ? $", {result.XmlElementCount} XML elements" : string.Empty;
                SuccessMessage = $"Exported {result.ExportedCount} document(s), skipped {result.SkippedCount}" +
                    $"{xmlNote} to {result.FilePath}";
            }
            else
            {
                ErrorMessage = result.Error ?? "Export failed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await UpdatePreviewAsync();
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Choose the export file name and folder",
                InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                FileName = "TallyExport.xml",
                Filter = "All files (*.*)|*.*",
            };
            if (dialog.ShowDialog() == true)
                OutputFolder = Path.GetDirectoryName(dialog.FileName) ?? OutputFolder;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open the folder picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(LastExportPath) || !File.Exists(LastExportPath))
        {
            ErrorMessage = "Export a file first, then use Open folder.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastExportPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open the folder: {ex.Message}";
        }
    }
}
