namespace RetailBillingApp.Services;

/// <summary>
/// RPT-13: one tabular report ready for PDF/Excel export. Deliberately dumb data —
/// cells are pre-formatted strings so the exporters stay generic and never depend
/// on a report type or the database.
/// </summary>
public class ReportExportSheet
{
    /// <summary>Document / worksheet title, also used for the output file name.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>Column captions for the table (must be non-empty for a renderable table).</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>Data rows, in column order (pad short rows with empty strings).</summary>
    public List<ReportExportRow> Rows { get; set; } = new();
}

/// <summary>One row of a <see cref="ReportExportSheet"/>; <see cref="IsTotal"/> rows get a totals style.</summary>
public class ReportExportRow
{
    public List<string> Cells { get; set; } = new();

    public bool IsTotal { get; set; }

    public ReportExportRow() { }

    /// <summary>Accepts any cell objects (strings, numbers, dates) and stringifies them.</summary>
    public ReportExportRow(IEnumerable<object?> cells, bool isTotal = false)
    {
        Cells = cells.Select(c => c?.ToString() ?? string.Empty).ToList();
        IsTotal = isTotal;
    }
}

/// <summary>
/// RPT-13: generic PDF (QuestPDF) and Excel (ClosedXML) exporters for any
/// <see cref="ReportExportSheet"/> — no DbContext, no report-specific logic.
/// </summary>
public interface IReportExportService
{
    /// <summary>
    /// Renders the sheet as a simple titled table and writes the PDF under
    /// %TEMP%\RetailBillingReports. Throws on failure (caller catches).
    /// </summary>
    Task<string> ExportPdfAsync(ReportExportSheet sheet);

    /// <summary>
    /// Writes the sheet as an .xlsx (bold frozen header row, autofit) in the same
    /// folder. Throws on failure (caller catches).
    /// </summary>
    Task<string> ExportExcelAsync(ReportExportSheet sheet);
}
