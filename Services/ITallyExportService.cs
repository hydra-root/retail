using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>SEC-06: what the export produces - one block per document family.</summary>
public enum TallyDocumentType
{
    Sales,
    Purchases,
    Journal,
    PaymentsReceipts,
    Masters,
    Combined
}

/// <summary>SEC-06: file flavour. TallyXml imports into TallyPrime; Csv/SpreadsheetXml open in Excel.</summary>
public enum TallyExportFormat
{
    TallyXml,
    Csv
}

public class TallyExportResult
{
    public bool Success { get; set; }

    public string? FilePath { get; set; }

    /// <summary>Number of XML elements written (0 for CSV so the accountant sees it is not an XML file).</summary>
    public int XmlElementCount { get; set; }

    public int ExportedCount { get; set; }

    public int SkippedCount { get; set; }

    public List<string> Warnings { get; } = new();

    public string? Error { get; set; }
}

public class TallyPreviewResult
{
    public int DocumentCount { get; set; }

    public int SkippedCount { get; set; }

    /// <summary>The exact skips a matching export would report, before the accountant runs one.</summary>
    public List<string> Warnings { get; } = new();

    public string? Error { get; set; }
}

/// <summary>
/// SEC-06: Tally XML + Excel-friendly CSV exports. Every method never throws -
/// failures come back as Success=false with Error set, because a broken export
/// must explain itself in the UI, not crash the export click.
/// </summary>
public interface ITallyExportService
{
    Task<TallyExportResult> ExportSalesXmlAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    Task<TallyExportResult> ExportPurchaseXmlAsync(DateTime from, DateTime to, string? targetDirectory = null);

    Task<TallyExportResult> ExportJournalXmlAsync(DateTime from, DateTime to, string? targetDirectory = null);

    Task<TallyExportResult> ExportPaymentReceiptXmlAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    Task<TallyExportResult> ExportMastersXmlAsync(string? targetDirectory = null);

    Task<TallyExportResult> ExportCombinedXmlAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    Task<TallyExportResult> ExportSalesCsvAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    Task<TallyExportResult> ExportPurchaseCsvAsync(DateTime from, DateTime to, string? targetDirectory = null);

    Task<TallyExportResult> ExportDayBookCsvAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    /// <summary>SpreadsheetML 2003 (.xml that Excel opens natively). A real .xlsx needs a zip package we cannot add.</summary>
    Task<TallyExportResult> ExportDayBookSpreadsheetXmlAsync(DateTime from, DateTime to, int? storeId = null, string? targetDirectory = null);

    /// <summary>Counters for the view's preview block - counts documents exactly as the matching export would.</summary>
    Task<TallyPreviewResult> PreviewAsync(TallyDocumentType docType, TallyExportFormat format, DateTime from, DateTime to, int? storeId = null);
}
