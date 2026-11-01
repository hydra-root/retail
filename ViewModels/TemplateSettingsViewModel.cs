using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INV-05: customizable invoice templates — template name, logo, header/
/// footer/terms text, tax breakup and signature options, with a live
/// plain-text preview of the printable template.
/// </summary>
public partial class TemplateSettingsViewModel : BaseViewModel
{
    private readonly ITemplateService _templateService;

    /// <summary>The loaded singleton row, kept for updates on save.</summary>
    private InvoiceTemplateSetting _settings = new();

    [ObservableProperty]
    private ObservableCollection<string> _templateNames = new() { "Thermal 80mm", "A4" };

    [ObservableProperty]
    private string _templateName = "Thermal 80mm";

    [ObservableProperty]
    private string _logoPath = string.Empty;

    [ObservableProperty]
    private bool _showLogo = true;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private string _termsText = string.Empty;

    [ObservableProperty]
    private bool _showTaxBreakup = true;

    [ObservableProperty]
    private bool _showSignatureLine = true;

    [ObservableProperty]
    private string _signatureCaption = "Authorised Signatory";

    /// <summary>Read-only monospace preview of the printable template.</summary>
    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public TemplateSettingsViewModel(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var settings = await _templateService.GetSettingsAsync();
            _settings = settings;

            TemplateName = settings.TemplateName;
            LogoPath = settings.LogoPath ?? string.Empty;
            ShowLogo = settings.ShowLogo;
            HeaderText = settings.HeaderText ?? string.Empty;
            FooterText = settings.FooterText ?? string.Empty;
            TermsText = settings.TermsText ?? string.Empty;
            ShowTaxBreakup = settings.ShowTaxBreakup;
            ShowSignatureLine = settings.ShowSignatureLine;
            SignatureCaption = settings.SignatureCaption ?? string.Empty;

            SuccessMessage = string.Empty;
            ErrorMessage = string.Empty;
            await RefreshPreviewAsync();
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

    /// <summary>Snapshot of the edited values, used for the unsaved-changes preview.</summary>
    private InvoiceTemplateSetting BuildSnapshot() => new()
    {
        Id = _settings.Id,
        TemplateName = TemplateName,
        LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath,
        ShowLogo = ShowLogo,
        HeaderText = HeaderText,
        FooterText = FooterText,
        TermsText = TermsText,
        ShowTaxBreakup = ShowTaxBreakup,
        ShowSignatureLine = ShowSignatureLine,
        SignatureCaption = SignatureCaption
    };

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        try
        {
            PreviewText = await _templateService.GeneratePreviewAsync(BuildSnapshot());
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
        }
    }

    /// <summary>INV-05: pick a PNG/JPG logo and copy it into .\Brand\.</summary>
    [RelayCommand]
    private async Task BrowseLogoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose Invoice Logo",
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = false
        };
        if (dialog.ShowDialog() != true) return;

        var (ok, result) = await _templateService.CopyLogoToAppFolderAsync(dialog.FileName);
        if (ok)
        {
            LogoPath = result ?? string.Empty;
            SuccessMessage = "Logo copied to the app Brand folder.";
            ErrorMessage = string.Empty;
            await RefreshPreviewAsync();
        }
        else
        {
            ErrorMessage = result ?? "Could not copy the logo.";
            SuccessMessage = string.Empty;
        }
    }

    /// <summary>INV-05: persist all template settings back to the singleton row.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _settings.TemplateName = TemplateName;
            _settings.LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath;
            _settings.ShowLogo = ShowLogo;
            _settings.HeaderText = HeaderText;
            _settings.FooterText = FooterText;
            _settings.TermsText = TermsText;
            _settings.ShowTaxBreakup = ShowTaxBreakup;
            _settings.ShowSignatureLine = ShowSignatureLine;
            _settings.SignatureCaption = SignatureCaption;

            var (ok, error) = await _templateService.SaveSettingsAsync(_settings);
            if (ok)
            {
                SuccessMessage = $"Template settings saved ({TemplateName}).";
                ErrorMessage = string.Empty;
                await RefreshPreviewAsync();
            }
            else
            {
                ErrorMessage = error ?? "Save failed.";
                SuccessMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Toggle preview-sensitive options to refresh the preview immediately.</summary>
    partial void OnShowLogoChanged(bool value) => _ = RefreshPreviewIfLoadedAsync();

    partial void OnShowTaxBreakupChanged(bool value) => _ = RefreshPreviewIfLoadedAsync();

    partial void OnShowSignatureLineChanged(bool value) => _ = RefreshPreviewIfLoadedAsync();

    partial void OnTemplateNameChanged(string value) => _ = RefreshPreviewIfLoadedAsync();

    private async System.Threading.Tasks.Task RefreshPreviewIfLoadedAsync()
    {
        if (_settings.Id > 0 && !IsLoading)
            await RefreshPreviewAsync();
    }
}
