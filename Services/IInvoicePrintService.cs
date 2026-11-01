using System.Collections.Generic;
using System.Windows.Documents;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-06 / INV-08: the print layouts a document can be rendered into.
/// "Thermal 80mm" maps to the fixed-width text path, the others to the
/// paginated FlowDocument (A4 / half-sheet) path.
/// </summary>
public enum InvoicePrintLayout
{
    Thermal80Mm,
    HalfSheet,
    A4
}

/// <summary>
/// A print-ready view of one invoice, decoupled from the EF entities so the
/// text renderer and the FlowDocument builder never re-query or re-round. Every
/// monetary field is the stored decimal verbatim — the printed totals are the
/// invoice totals by construction.
/// </summary>
public class InvoicePrintDocument
{
    // ---- company / header block (from InvoiceTemplateSetting) ----
    public List<string> CompanyHeaderLines { get; set; } = new();
    public bool ShowLogo { get; set; }
    /// <summary>Absolute path of the logo to print, or null when missing/not configured.</summary>
    public string? LogoAbsolutePath { get; set; }
    public string? FooterText { get; set; }
    public string? TermsText { get; set; }
    public bool ShowSignatureLine { get; set; }
    public string SignatureCaption { get; set; } = "Authorised Signatory";

    // ---- invoice meta ----
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceDateDisplay { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsEstimate { get; set; }
    public bool IsBillOfSupply { get; set; }
    public bool ShowTaxBreakup { get; set; }
    public bool HasInterStateTax { get; set; }

    /// <summary>ESTIMATE / BILL OF SUPPLY / TAX INVOICE header label.</summary>
    public string DocumentTitle { get; set; } = "TAX INVOICE";

    // ---- customer block ----
    public string CustomerName { get; set; } = "Walk-in Customer";
    public string? CustomerGstin { get; set; }
    public string? PlaceOfSupply { get; set; }

    // ---- items ----
    public List<InvoicePrintRow> Items { get; set; } = new();

    // ---- totals (stored decimals, formatted only at render time) ----
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal WriteOffAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>TotalAmount - PaidAmount - WriteOff, clamped at zero (shown as Balance Due).</summary>
    public decimal BalanceDue { get; set; }

    // ---- layout ----
    public InvoicePrintLayout Layout { get; set; } = InvoicePrintLayout.Thermal80Mm;
    /// <summary>Fixed print columns for the thermal path (42, matching TemplateService).</summary>
    public int Columns { get; set; } = 42;
    /// <summary>Human-readable layout name shown in the preview toolbar.</summary>
    public string LayoutDisplayName { get; set; } = "Thermal 80mm";

    // ---- copies ("COPY n OF m"; 1/1 means no marker) ----
    public int CurrentCopy { get; set; } = 1;
    public int TotalCopies { get; set; } = 1;

    public bool HasCopyMarker => TotalCopies > 1;
    public bool IsThermal => Layout == InvoicePrintLayout.Thermal80Mm;
}

/// <summary>One invoice line, ready to print. Amounts are the stored line decimals.</summary>
public class InvoicePrintRow
{
    public int Serial { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? HsnCode { get; set; }
    public int Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal Amount { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public bool TaxInclusive { get; set; }

    /// <summary>CGST+SGST or IGST for the line, whichever applies (inter-state aware).</summary>
    public decimal DisplayTax => Igst > 0m ? Igst : Cgst + Sgst;
}

/// <summary>Result of a print attempt: never throws — carries cancel/fallback info.</summary>
public class PrintOutcome
{
    public bool Success { get; set; }
    public bool Cancelled { get; set; }
    public string? Message { get; set; }
    /// <summary>Desktop .txt path when printing fell back to a text file.</summary>
    public string? SavedTextPath { get; set; }
    public int CopiesSent { get; set; }
}

/// <summary>
/// INV-06 / INV-08: build a print-ready document from a stored invoice, render it
/// either as fixed-width thermal text or as a paginated A4 / half-sheet FlowDocument,
/// and send it to a printer through WPF's in-framework PrintDialog.
/// </summary>
public interface IInvoicePrintService
{
    /// <summary>Human-readable reason for the most recent failure (mirrors IInvoiceService).</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking messages accumulated by the last Build/Print call.</summary>
    List<string> Errors { get; }

    /// <summary>
    /// Load the invoice + items + customer, resolve the layout (per-invoice
    /// override wins, otherwise the saved template paper size) and return a
    /// print-ready document. Throws InvalidOperationException with a friendly
    /// message when the invoice does not exist.
    /// </summary>
    Task<InvoicePrintDocument> BuildAsync(int invoiceId, string? layoutOverride = null);

    /// <summary>Thermal-80mm path: fixed-width plain text (~42 columns).</summary>
    string RenderText(InvoicePrintDocument doc);

    /// <summary>A4 / half-sheet path: a real paginated FlowDocument.</summary>
    FlowDocument BuildFlowDocument(InvoicePrintDocument doc);

    /// <summary>
    /// Print via PrintDialog. Thermal renders the text onto a narrow (80mm) page;
    /// A4 / half-sheet print the FlowDocument. On dialog cancel or no printer the
    /// text render is written to a Desktop file and its path is reported. Never throws.
    /// </summary>
    Task<PrintOutcome> PrintAsync(InvoicePrintDocument doc, int copies, bool usePreview);
}
