namespace RetailBillingApp.Services;

/// <summary>
/// STK-12: bulk import/export. CSV import of Products and Customers with
/// validation (preview without writes), one-transaction commit with dedupe,
/// sample template export and full CSV export to the Desktop.
/// </summary>
public interface IBulkImportService
{
    /// <summary>
    /// Parse and validate a CSV file for "Products" or "Customers" without any
    /// database writes. Bad rows are listed in Errors with row numbers.
    /// </summary>
    Task<BulkImportResult> ValidateCsvAsync(string entityType, string filePath);

    /// <summary>
    /// Validate then insert all valid rows inside a single database transaction
    /// (Products dedupe on SKU, Customers dedupe on Name + Phone).
    /// </summary>
    Task<BulkImportResult> CommitAsync(string entityType, string filePath);

    /// <summary>Write a 3-row sample CSV template to the Desktop and return its path.</summary>
    Task<string> ExportTemplateCsvAsync(string entityType);

    /// <summary>Export all current rows of the entity type to the Desktop and return its path.</summary>
    Task<string> ExportAllCsvAsync(string entityType);

    /// <summary>
    /// STK-12 supplier flavor: parse and validate a Suppliers CSV without any database
    /// writes (dedupe key: supplier name). Bad rows are listed in Errors with row numbers.
    /// </summary>
    Task<BulkImportResult> ValidateSupplierCsvAsync(string filePath);

    /// <summary>Validate then insert all valid supplier rows inside one database transaction.</summary>
    Task<BulkImportResult> CommitSupplierCsvAsync(string filePath);

    /// <summary>Write a 3-row sample Suppliers CSV template to the Desktop and return its path.</summary>
    Task<string> ExportSupplierTemplateCsvAsync();

    /// <summary>Export all current suppliers to the Desktop and return its path.</summary>
    Task<string> ExportSupplierAllCsvAsync();
}

/// <summary>STK-12: outcome of a bulk import preview or commit.</summary>
public class BulkImportResult
{
    public int Imported { get; set; }

    public int Failed { get; set; }

    /// <summary>One message per rejected row, prefixed with its CSV row number.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Per-row preview result (validation only; null after a commit).</summary>
    public List<BulkImportRow>? SampleRows { get; set; }
}

/// <summary>STK-12: one CSV row as shown in the preview grid.</summary>
public class BulkImportRow
{
    /// <summary>CSV row number (1-based, counting from the header).</summary>
    public int Row { get; set; }

    /// <summary>"OK" or "Error".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Empty for valid rows, otherwise the reason the row was rejected.</summary>
    public string Error { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Key fields joined for display (SKU/Rate/Tax for products, Phone/GSTIN for customers).</summary>
    public string Details { get; set; } = string.Empty;
}
