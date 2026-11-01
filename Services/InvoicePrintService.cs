using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Print;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-06 / INV-08: builds a print-ready document from a stored invoice and renders /
/// prints it. Thermal 80mm uses the fixed-width text renderer; A4 / half-sheet use the
/// paginated FlowDocument. Printing goes through WPF's in-framework PrintDialog only.
/// Monetary values are never re-derived — printed totals equal the invoice totals.
/// </summary>
public class InvoicePrintService : IInvoicePrintService
{
    private readonly AppDbContext _dbContext;
    private readonly ITemplateService _templateService;

    public string? LastValidationError { get; private set; }
    public List<string> Errors { get; } = new();

    public InvoicePrintService(AppDbContext dbContext, ITemplateService templateService)
    {
        _dbContext = dbContext;
        _templateService = templateService;
    }

    // ------------------------------ ESC/POS stub ------------------------------
    // v1.1 owns raw ESC/POS byte-stream thermal printing (cut / beep / codepage /
    // native raster logo). v1.1 should replace the FormattedText thermal page in
    // PrintThermalAsync with a true ESC/POS job. Intentionally left unimplemented.
    private static Task PrintEscPosRawBytesAsync(InvoicePrintDocument doc)
        => throw new NotImplementedException("INV-08: ESC/POS raw-byte thermal printing is owned by v1.1.");

    // ------------------------------- BuildAsync -------------------------------

    public async Task<InvoicePrintDocument> BuildAsync(int invoiceId, string? layoutOverride = null)
    {
        LastValidationError = null;
        Errors.Clear();

        var invoice = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
        {
            LastValidationError = $"Invoice #{invoiceId} was not found — it may have been deleted.";
            throw new InvalidOperationException(LastValidationError);
        }

        var settings = await _templateService.GetSettingsAsync();
        var layout = ResolveLayout(layoutOverride, settings.TemplateName);

        var doc = Map(invoice, settings, layout);

        if (invoice.Items.Count == 0)
            Errors.Add("This document has no line items; it will print with an empty items table.");

        // Logo is skipped (not an error) when the file is missing — resolve silently.
        if (settings.ShowLogo && string.IsNullOrWhiteSpace(doc.LogoAbsolutePath)
            && !string.IsNullOrWhiteSpace(settings.LogoPath))
            Errors.Add($"Logo file '{settings.LogoPath}' was not found; printing without the logo.");

        return doc;
    }

    private InvoicePrintDocument Map(Models.Invoice invoice, Models.InvoiceTemplateSetting settings, InvoicePrintLayout layout)
    {
        var headerLines = (settings.HeaderText ?? string.Empty)
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (headerLines.Count == 0)
            headerLines.Add("Retail Billing");

        var title = invoice.IsEstimate ? "ESTIMATE"
            : invoice.IsBillOfSupply ? "BILL OF SUPPLY"
            : "TAX INVOICE";

        var balance = invoice.TotalAmount - invoice.PaidAmount - invoice.WriteOffAmount;
        if (balance < 0m) balance = 0m;

        var doc = new InvoicePrintDocument
        {
            CompanyHeaderLines = headerLines,
            ShowLogo = settings.ShowLogo,
            LogoAbsolutePath = settings.ShowLogo ? ResolveLogoPath(settings.LogoPath) : null,
            FooterText = settings.FooterText,
            TermsText = settings.TermsText,
            ShowSignatureLine = settings.ShowSignatureLine,
            SignatureCaption = string.IsNullOrWhiteSpace(settings.SignatureCaption) ? "Authorised Signatory" : settings.SignatureCaption,

            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDateDisplay = invoice.InvoiceDate.ToLocalTime().ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
            PaymentMode = invoice.PaymentMode,
            Status = invoice.Status,
            IsEstimate = invoice.IsEstimate,
            IsBillOfSupply = invoice.IsBillOfSupply,
            ShowTaxBreakup = settings.ShowTaxBreakup,
            HasInterStateTax = invoice.IGST > 0m,
            DocumentTitle = title,

            CustomerName = invoice.Customer?.Name ?? invoice.CustomerName ?? "Walk-in Customer",
            CustomerGstin = string.IsNullOrWhiteSpace(invoice.CustomerGSTIN) ? null : invoice.CustomerGSTIN,
            PlaceOfSupply = string.IsNullOrWhiteSpace(invoice.PlaceOfSupply) ? null : invoice.PlaceOfSupply,

            Items = invoice.Items
                .Select((it, idx) => new InvoicePrintRow
                {
                    Serial = idx + 1,
                    ProductName = it.ProductName,
                    HsnCode = it.HSNCode,
                    Quantity = it.Quantity,
                    Rate = it.Rate,
                    TaxPercentage = it.TaxPercentage,
                    Amount = it.Amount,
                    Cgst = it.CGST,
                    Sgst = it.SGST,
                    Igst = it.IGST,
                    Cess = it.Cess,
                    TaxAmount = it.TaxAmount,
                    Total = it.Total,
                    TaxInclusive = it.TaxInclusive
                })
                .ToList(),

            SubTotal = invoice.SubTotal,
            Discount = invoice.Discount,
            Cgst = invoice.CGST,
            Sgst = invoice.SGST,
            Igst = invoice.IGST,
            Cess = invoice.Cess,
            TaxAmount = invoice.TaxAmount,
            RoundOff = invoice.RoundOff,
            PaidAmount = invoice.PaidAmount,
            WriteOffAmount = invoice.WriteOffAmount,
            TotalAmount = invoice.TotalAmount,
            BalanceDue = balance,

            Layout = layout,
            Columns = 42,
            LayoutDisplayName = LayoutName(layout),
            CurrentCopy = 1,
            TotalCopies = 1
        };
        return doc;
    }

    private static InvoicePrintLayout ResolveLayout(string? overrideLayout, string templateName)
    {
        var candidate = string.IsNullOrWhiteSpace(overrideLayout) ? templateName : overrideLayout;
        var norm = (candidate ?? string.Empty).Trim().ToLowerInvariant();

        if (norm.Contains("half")) return InvoicePrintLayout.HalfSheet;
        if (norm.Contains("thermal") || norm.Contains("80") || norm.Contains("receipt")) return InvoicePrintLayout.Thermal80Mm;
        if (norm.Contains("a4")) return InvoicePrintLayout.A4;
        return InvoicePrintLayout.Thermal80Mm; // default to the counter receipt
    }

    private static string LayoutName(InvoicePrintLayout layout) => layout switch
    {
        InvoicePrintLayout.HalfSheet => "Half Sheet",
        InvoicePrintLayout.A4 => "A4",
        _ => "Thermal 80mm"
    };

    /// <summary>Settings store a relative path ("Brand\logo.png"); resolve to absolute
    /// against the app folder. Returns null when not configured or the file is missing.</summary>
    private static string? ResolveLogoPath(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath)) return null;
        try
        {
            var cleaned = logoPath.Trim().TrimStart('.');
            var full = Path.IsPathRooted(cleaned)
                ? cleaned
                : Path.Combine(AppContext.BaseDirectory, cleaned);
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    // ----------------------------- Render / Flow -----------------------------

    public string RenderText(InvoicePrintDocument doc) => InvoiceTextRenderer.Render(doc, doc.Columns);

    public FlowDocument BuildFlowDocument(InvoicePrintDocument doc) => InvoiceDocumentBuilder.Build(doc);

    // ------------------------------- PrintAsync -------------------------------

    public async Task<PrintOutcome> PrintAsync(InvoicePrintDocument doc, int copies, bool usePreview)
    {
        LastValidationError = null;
        if (copies < 1) copies = 1;

        try
        {
            if (!HasPrinterAvailable())
                return await FallbackToDesktopAsync(doc, "No printer is available on this machine.");

            var dialog = new PrintDialog();
            if (usePreview && dialog.ShowDialog() != true)
                return await FallbackToDesktopAsync(doc, "Print dialog was cancelled.");

            var caption = $"{doc.DocumentTitle} {doc.InvoiceNumber}";
            for (var c = 1; c <= copies; c++)
            {
                doc.CurrentCopy = c;
                doc.TotalCopies = copies;

                var job = doc.IsThermal
                    ? BuildThermalPage(doc)
                    : BuildFlowPaginator(doc);

                dialog.PrintDocument(job.Paginator, copies > 1 ? $"{caption} — copy {c}/{copies}" : caption);
            }

            doc.CurrentCopy = 1;
            doc.TotalCopies = 1;

            return new PrintOutcome
            {
                Success = true,
                CopiesSent = copies,
                Message = copies > 1
                    ? $"Sent {copies} copies of {doc.InvoiceNumber} to the printer."
                    : $"Sent {doc.InvoiceNumber} to the printer."
            };
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return await FallbackToDesktopAsync(doc, $"Printing failed ({ex.Message}); the text was saved instead.");
        }
    }

    private async Task<PrintOutcome> FallbackToDesktopAsync(InvoicePrintDocument doc, string reason)
    {
        doc.CurrentCopy = 1;
        doc.TotalCopies = 1;
        var path = await WriteTextToDesktopAsync(doc);
        return new PrintOutcome
        {
            Success = path != null,
            Cancelled = reason.Contains("cancelled", StringComparison.OrdinalIgnoreCase),
            Message = path != null ? $"{reason} Saved text to: {path}" : reason,
            SavedTextPath = path
        };
    }

    /// <summary>Existing project convention (PAY-03 / PAY-07): write a printable text
    /// render to the Desktop and report the path. Returns the path, or null on failure.</summary>
    private static async Task<string?> WriteTextToDesktopAsync(InvoicePrintDocument doc)
    {
        try
        {
            var safe = string.Concat(doc.InvoiceNumber.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
            var name = string.IsNullOrWhiteSpace(safe) ? $"DOC-{DateTime.Now:yyyyMMddHHmmss}" : safe;
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Invoice_{name}_{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, InvoiceTextRenderer.Render(doc, doc.Columns));
            return path;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------- thermal page build ----------------------------

    private (DocumentPaginator Paginator, double Height) BuildThermalPage(InvoicePrintDocument doc)
    {
        const double fontSize = 8.0;
        const double pad = 6;
        var widthDiu = MmToDiu(80);

        var text = InvoiceTextRenderer.Render(doc, doc.Columns);
        var lineCount = Math.Max(1, text.Replace("\r", "").Split('\n').Length);
        var lineH = fontSize * 1.45;
        var heightDiu = Math.Max(MmToDiu(60), pad * 2 + lineCount * lineH);

        var page = new FixedPage { Width = widthDiu, Height = heightDiu };
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            TextWrapping = TextWrapping.NoWrap,
            LineHeight = lineH,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        tb.Width = widthDiu - pad * 2;
        tb.Measure(new Size(widthDiu - pad * 2, double.PositiveInfinity));
        tb.Arrange(new Rect(pad, pad, widthDiu - pad * 2, Math.Max(tb.DesiredSize.Height, lineCount * lineH)));
        FixedPage.SetLeft(tb, pad);
        FixedPage.SetTop(tb, pad);
        page.Children.Add(tb);
        page.Measure(new Size(widthDiu, heightDiu));
        page.Arrange(new Rect(0, 0, widthDiu, heightDiu));
        page.UpdateLayout();

        var content = new PageContent();
        ((System.Windows.Markup.IAddChild)content).AddChild(page);

        var fd = new FixedDocument();
        fd.Pages.Add(content);

        return (fd.DocumentPaginator, heightDiu);
    }

    // --------------------------- flow document build ---------------------------

    private const double A4WidthDiu = 794;
    private const double A4HeightDiu = 1122;
    private const double A5WidthDiu = 559;
    private const double A5HeightDiu = 794;

    private (DocumentPaginator Paginator, double Height) BuildFlowPaginator(InvoicePrintDocument doc)
    {
        var flow = InvoiceDocumentBuilder.Build(doc);
        var isHalf = doc.Layout == InvoicePrintLayout.HalfSheet;
        // FlowDocument is a FrameworkContentElement (no Measure/Arrange); setting the
        // page size + using its DocumentPaginator is the standard paginated-print path.
        flow.PageWidth = isHalf ? A5WidthDiu : A4WidthDiu;
        flow.PageHeight = isHalf ? A5HeightDiu : A4HeightDiu;

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        return (paginator, isHalf ? A5HeightDiu : A4HeightDiu);
    }

    // ------------------------------- printer probe -------------------------------

    private static bool HasPrinterAvailable()
    {
        try
        {
            using var server = new System.Printing.LocalPrintServer();
            var queues = server.GetPrintQueues(
                new[] { System.Printing.EnumeratedPrintQueueTypes.Local,
                        System.Printing.EnumeratedPrintQueueTypes.Connections });
            return queues != null && queues.Any();
        }
        catch
        {
            // If we cannot enumerate (rare / no permission), let PrintDialog decide.
            return true;
        }
    }

    private static double MmToDiu(double mm) => mm / 25.4 * 96.0;
}
