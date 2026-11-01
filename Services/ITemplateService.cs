using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-05: customizable invoice template settings — singleton settings row,
/// logo copy-in, and plain-text preview of the thermal / A4 invoice template.
/// </summary>
public interface ITemplateService
{
    /// <summary>Load the settings row, creating one with defaults if none exists yet.</summary>
    Task<InvoiceTemplateSetting> GetSettingsAsync();

    /// <summary>Persist the settings row. Returns (false, error) on failure.</summary>
    Task<(bool Success, string? Error)> SaveSettingsAsync(InvoiceTemplateSetting setting);

    /// <summary>
    /// Plain-text preview of the invoice header + footer block using the
    /// given settings (or the saved ones when null) and the last invoice
    /// summary (or dummy data when no invoice exists yet).
    /// </summary>
    Task<string> PreviewTextHeaderAsync(InvoiceTemplateSetting? settings = null);

    /// <summary>
    /// Full formatted text template the printing feature consumes:
    /// header, invoice/customer block, items table, optional tax breakup,
    /// footer note, terms and signature line.
    /// </summary>
    Task<string> GeneratePreviewAsync(InvoiceTemplateSetting? overrideSettings = null);

    /// <summary>
    /// Copy a chosen logo file into the app's .\Brand\ folder as logo&lt;ext&gt;.
    /// Returns (true, relative path such as "Brand\logo.png") on success or
    /// (false, error message) on failure.
    /// </summary>
    Task<(bool Success, string? RelativePathOrError)> CopyLogoToAppFolderAsync(string sourcePath);
}
