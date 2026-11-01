using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INV-06 / INV-08: the print-preview window's view model. Lets the shopkeeper pick a
/// layout (thermal / half-sheet / A4), set the copy count, see a faithful preview of the
/// invoice, print it (thermal text page or paginated FlowDocument), or fall back to
/// saving the text render to the Desktop. The document itself is built by the injected
/// IInvoicePrintService — the VM only holds preview state and issues commands.
/// </summary>
public partial class InvoicePrintPreviewViewModel : BaseViewModel
{
    private readonly IInvoicePrintService _printService;
    private readonly ITemplateService _templateService;

    /// <summary>Set by the window so the VM can dismiss it without referencing WPF directly.</summary>
    public Action? CloseAction { get; set; }

    /// <summary>The invoice being previewed (set by the coordinator / window).</summary>
    public int InvoiceId { get; private set; }

    public List<string> Layouts { get; } = new() { "Thermal 80mm", "Half Sheet", "A4" };

    [ObservableProperty]
    private string _selectedLayout = "Thermal 80mm";

    [ObservableProperty]
    private int _copies = 1;

    [ObservableProperty]
    private string _printerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThermal))]
    private InvoicePrintDocument? _document;

    /// <summary>Paginated preview for A4 / half-sheet (null for thermal).</summary>
    [ObservableProperty]
    private FlowDocument? _previewDocument;

    /// <summary>Fixed-width thermal text (null for A4 / half-sheet).</summary>
    [ObservableProperty]
    private string _thermalText = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when the current preview is the thermal text path.</summary>
    public bool IsThermal => Document?.IsThermal == true || SelectedLayout == "Thermal 80mm";

    public string Title => Document == null
        ? "Print Preview"
        : $"Print Preview — {Document.DocumentTitle} {Document.InvoiceNumber}";

    public InvoicePrintPreviewViewModel(IInvoicePrintService printService, ITemplateService templateService)
    {
        _printService = printService;
        _templateService = templateService;
    }

    /// <summary>Entry point called by the window's Loaded handler.</summary>
    public async Task InitializeAsync(int invoiceId, string? layoutOverride = null)
    {
        InvoiceId = invoiceId;
        if (!string.IsNullOrWhiteSpace(layoutOverride))
            SelectedLayout = NormalizeLayout(layoutOverride);
        else
            await DefaultLayoutFromTemplateAsync();
        await Refresh();
    }

    private async Task DefaultLayoutFromTemplateAsync()
    {
        try
        {
            var settings = await _templateService.GetSettingsAsync();
            SelectedLayout = NormalizeLayout(settings.TemplateName);
        }
        catch
        {
            SelectedLayout = "Thermal 80mm";
        }
    }

    private static string NormalizeLayout(string? raw)
    {
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n.Contains("half")) return "Half Sheet";
        if (n.Contains("a4")) return "A4";
        return "Thermal 80mm";
    }

    partial void OnSelectedLayoutChanged(string value) => OnPropertyChanged(nameof(IsThermal));

    [RelayCommand]
    public async Task Refresh()
    {
        if (InvoiceId <= 0) return;
        IsLoading = true;
        try
        {
            var doc = await _printService.BuildAsync(InvoiceId, SelectedLayout);
            doc.TotalCopies = Copies > 1 ? Copies : 1;
            doc.CurrentCopy = 1;
            Document = doc;

            if (doc.IsThermal)
            {
                ThermalText = _printService.RenderText(doc);
                PreviewDocument = null;
            }
            else
            {
                ThermalText = string.Empty;
                PreviewDocument = _printService.BuildFlowDocument(doc);
            }

            ErrorMessage = _printService.Errors.Count > 0 ? string.Join("  ", _printService.Errors) : string.Empty;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsThermal));
        }
        catch (Exception ex)
        {
            ErrorMessage = _printService.LastValidationError ?? ex.Message;
            Document = null;
            PreviewDocument = null;
            ThermalText = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Print()
    {
        if (Document == null)
        {
            ErrorMessage = "Nothing to print — the document could not be built.";
            return;
        }

        IsLoading = true;
        try
        {
            // usePreview=true shows the OS print dialog (the in-app preview already rendered).
            var outcome = await _printService.PrintAsync(Document, Copies, usePreview: true);

            if (outcome.Success && outcome.SavedTextPath == null)
            {
                SuccessMessage = outcome.Message ?? "Sent to the printer.";
                ErrorMessage = string.Empty;
            }
            else if (outcome.SavedTextPath != null)
            {
                ErrorMessage = string.Empty;
                SuccessMessage = outcome.Message ?? "Printed to a text file on the Desktop.";
            }
            else if (outcome.Cancelled)
            {
                ErrorMessage = outcome.Message ?? "Print cancelled.";
                SuccessMessage = string.Empty;
            }
            else
            {
                ErrorMessage = outcome.Message ?? "Printing failed.";
                SuccessMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            // Rebuild after printing resets copy markers internally.
            await Refresh();
        }
    }

    /// <summary>Fallback of the fallback: save the thermal text render to the Desktop.</summary>
    [RelayCommand]
    private async Task SaveAsTextFile()
    {
        if (Document == null)
        {
            ErrorMessage = "Nothing to save — the document could not be built.";
            return;
        }

        try
        {
            var text = _printService.RenderText(Document);
            var safe = string.Concat(Document.InvoiceNumber.Replace(" ", "-"));
            var fileName = $"Invoice_{safe}_{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            await File.WriteAllTextAsync(path, text);
            SuccessMessage = $"Saved to: {path}";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save the file: {ex.Message}";
            SuccessMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void Close() => CloseAction?.Invoke();
}
