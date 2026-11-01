using System.ComponentModel.DataAnnotations;

namespace RetailBillingApp.Models;

/// <summary>
/// INV-05: singleton row of customizable invoice template settings —
/// business branding, logo, header/footer text, terms, tax breakup and
/// signature line for the GST invoice / thermal text template.
/// Only one row is expected; services create it with defaults if missing.
/// </summary>
public class InvoiceTemplateSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Paper/print layout, e.g. "Thermal 80mm" or "A4".</summary>
    [MaxLength(50)]
    public string TemplateName { get; set; } = "Thermal 80mm";

    /// <summary>Relative path (.\Brand\logo*.png/jpg) of the logo shown at the top of the invoice.</summary>
    [MaxLength(500)]
    public string? LogoPath { get; set; }

    /// <summary>Whether the logo is printed on the invoice header.</summary>
    public bool ShowLogo { get; set; } = true;

    /// <summary>Business name / tagline printed at the top of the invoice.</summary>
    [MaxLength(200)]
    public string? HeaderText { get; set; }

    /// <summary>Footer note, e.g. bank details / UPI QR note.</summary>
    [MaxLength(1000)]
    public string? FooterText { get; set; }

    /// <summary>Terms &amp; conditions lines printed at the bottom.</summary>
    [MaxLength(1000)]
    public string? TermsText { get; set; }

    /// <summary>Whether the GST tax breakup (CGST/SGST/IGST/Cess) is printed.</summary>
    public bool ShowTaxBreakup { get; set; } = true;

    /// <summary>Whether a signature area is printed above the caption.</summary>
    public bool ShowSignatureLine { get; set; } = true;

    /// <summary>Caption under the signature space, e.g. "Authorised Signatory".</summary>
    [MaxLength(200)]
    public string? SignatureCaption { get; set; } = "Authorised Signatory";
}
