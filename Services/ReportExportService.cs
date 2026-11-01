using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// RPT-13: PDF (QuestPDF) + Excel (ClosedXML) export of a <see cref="ReportExportSheet"/>.
/// Stateless and database-free — the caller builds the sheet (from the same rows the
/// CSV builders walk) and only has to catch exceptions.
/// </summary>
public class ReportExportService : IReportExportService
{
    // Belt and braces around the App startup line: even if the App-level
    // QuestPDF.Settings.License assignment is ever missed, the Community license is
    // set before the first export here.
    static ReportExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // QuestPDF's default family is Lato, which standard Windows installs do not
        // ship (draw-time FontManagerException). Segoe UI is always present.
        QuestPDF.Settings.Font = "Segoe UI";
    }

    public Task<string> ExportPdfAsync(ReportExportSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var path = BuildOutputPath(sheet, ".pdf");
        Directory.CreateDirectory(ExportDirectory);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text(string.IsNullOrWhiteSpace(sheet.Title)
                        ? "Report" : sheet.Title.Trim())
                        .FontSize(16).Bold().FontColor(Colors.Grey.Darken3);

                    column.Item().PaddingTop(2)
                        .Text($"Generated {sheet.GeneratedAt:dd-MMM-yyyy HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingTop(10).PaddingVertical(5).Table(table =>
                {
                    var columns = sheet.Headers.Count == 0 ? 1 : sheet.Headers.Count;
                    table.ColumnsDefinition(cd =>
                    {
                        for (var i = 0; i < columns; i++)
                            cd.RelativeColumn();
                    });

                    if (sheet.Headers.Count > 0)
                    {
                        table.Header(header =>
                        {
                            foreach (var headerText in sheet.Headers)
                            {
                                header.Cell()
                                    .Background(Colors.Grey.Lighten2)
                                    .BorderBottom(1).BorderColor(Colors.Grey.Darken2)
                                    .Padding(4)
                                    .Text(headerText)
                                    .FontSize(9).Bold();
                            }
                        });
                    }

                    foreach (var row in sheet.Rows)
                    {
                        var cells = Enumerable.Range(0, columns).Select(i =>
                            i < row.Cells.Count ? row.Cells[i] : string.Empty).ToList();

                        foreach (var cell in cells)
                        {
                            table.Cell()
                                .Background(row.IsTotal ? Colors.Grey.Lighten4 : Colors.White)
                                .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
                                .Padding(4)
                                .Text(cell)
                                .FontSize(row.IsTotal ? 9.5f : 9f)
                                .FontWeight(row.IsTotal ? FontWeight.Bold : FontWeight.Normal);
                        }
                    }
                });

                page.Footer().AlignCenter().Text("Retail Billing App")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });

        document.GeneratePdf(path);
        return Task.FromResult(path);
    }

    public Task<string> ExportExcelAsync(ReportExportSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var path = BuildOutputPath(sheet, ".xlsx");
        Directory.CreateDirectory(ExportDirectory);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SanitizeSheetName(sheet.Title));

        var headers = sheet.Headers.Count == 0
            ? new List<string> { "Data" }
            : sheet.Headers;

        var startCol = 1;
        foreach (var header in headers)
        {
            worksheet.Cell(1, startCol).Value = header;
            startCol++;
        }

        worksheet.Range(1, 1, 1, headers.Count).Style.Font.SetBold();
        worksheet.SheetView.FreezeRows(1);

        var rowNumber = 2;
        foreach (var row in sheet.Rows)
        {
            var col = 1;
            for (var i = 0; i < headers.Count; i++)
            {
                worksheet.Cell(rowNumber, col).Value =
                    i < row.Cells.Count ? row.Cells[i] : string.Empty;
                col++;
            }

            if (row.IsTotal)
                worksheet.Range(rowNumber, 1, rowNumber, headers.Count).Style.Font.SetBold();

            rowNumber++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
        return Task.FromResult(path);
    }

    // ---- helpers ----

    /// <summary>%TEMP%\RetailBillingReports — same folder for PDF and Excel so the email body can name both.</summary>
    internal static string ExportDirectory =>
        Path.Combine(Path.GetTempPath(), "RetailBillingReports");

    private static string BuildOutputPath(ReportExportSheet sheet, string extension)
    {
        var title = SanitizeFileName(string.IsNullOrWhiteSpace(sheet.Title) ? "Report" : sheet.Title);
        var fileName = $"{title}-{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
        return Path.Combine(ExportDirectory, fileName);
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in title)
            sb.Append(invalid.Contains(c) ? '_' : c);

        var name = sb.ToString().Trim().Trim('.');
        return name.Length == 0 ? "Report" : (name.Length > 60 ? name[..60].Trim().Trim('.') : name);
    }

    /// <summary>Excel sheet names: max 31 chars, and []:*?/\ cannot appear (apostrophes dropped at both ends).</summary>
    private static string SanitizeSheetName(string title)
    {
        var invalid = new[] { '[', ']', '*', ':', '?', '/', '\\' };
        var sb = new StringBuilder();
        foreach (var c in title)
            sb.Append(invalid.Contains(c) ? ' ' : c);

        var name = sb.ToString().Trim().Trim('\'');
        if (name.Length == 0) name = "Report";
        return name.Length > 31 ? name[..31].Trim() : name;
    }
}
