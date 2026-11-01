using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.IO;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-05: customizable invoice templates — singleton settings row, logo
/// copy-in, and plain-text preview of the thermal (80mm, 42 columns) /
/// A4 invoice template that the printing feature consumes.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly AppDbContext _dbContext;

    /// <summary>Typical 80mm thermal print width in 12x24-font characters.</summary>
    private const int Width = 42;

    public TemplateService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<InvoiceTemplateSetting> GetSettingsAsync()
    {
        var settings = await _dbContext.Set<InvoiceTemplateSetting>().FirstOrDefaultAsync();
        if (settings != null) return settings;

        // First run: seed the singleton row with safe defaults
        settings = new InvoiceTemplateSetting
        {
            TemplateName = "Thermal 80mm",
            ShowLogo = true,
            HeaderText = "SHREE BALAJI TRADERS",
            FooterText = "Scan & pay via UPI  ·  Thank you for your business!",
            TermsText = "1. Goods once sold will not be taken back or exchanged.\n2. Warranty claims as per manufacturer policy only.",
            ShowTaxBreakup = true,
            ShowSignatureLine = true,
            SignatureCaption = "Authorised Signatory"
        };
        _dbContext.Set<InvoiceTemplateSetting>().Add(settings);
        await _dbContext.SaveChangesAsync();
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(InvoiceTemplateSetting setting)
    {
        try
        {
            _dbContext.Set<InvoiceTemplateSetting>().Update(setting);
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.InnerException?.Message ?? ex.Message);
        }
    }

    public async Task<string> PreviewTextHeaderAsync(InvoiceTemplateSetting? settings = null)
    {
        settings ??= await GetSettingsAsync();
        var invoice = await GetLastInvoiceOrDefaultAsync();
        return BuildHeader(settings, invoice) + Environment.NewLine + BuildFooter(settings, invoice, withSummaryTab: true);
    }

    public async Task<string> GeneratePreviewAsync(InvoiceTemplateSetting? overrideSettings = null)
    {
        var settings = overrideSettings ?? await GetSettingsAsync();
        var invoice = await GetLastInvoiceOrDefaultAsync();

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader(settings, invoice));
        sb.AppendLine(Divider());

        // Invoice meta + customer block
        sb.AppendLine(InLine("Invoice No:", Truncate(invoice.InvoiceNumber, 23)));
        sb.AppendLine(InLine("Date:", invoice.InvoiceDate.ToString("dd-MMM-yyyy")));
        sb.AppendLine(InLine("Type:", TypeLabel(invoice)));
        sb.AppendLine(InLine("Customer:", Truncate(invoice.Customer?.Name ?? invoice.CustomerName ?? "Walk-in Customer", 23)));
        sb.AppendLine(InLine("GSTIN:", Truncate(invoice.CustomerGSTIN ?? "-", 34)));
        sb.AppendLine(InLine("Place of Supply:", invoice.PlaceOfSupply ?? "-"));

        // Items table
        sb.AppendLine(Divider());
        sb.AppendLine(ItemsHeader());
        foreach (var item in invoice.Items)
            sb.AppendLine(ItemsLine(item));
        if (invoice.Items.Count == 0)
            sb.AppendLine("No items");
        sb.AppendLine(Divider());

        // Totals + optional tax breakup
        sb.AppendLine(InLine("Sub Total:", invoice.SubTotal));
        if (invoice.Discount > 0)
            sb.AppendLine(InLine("Discount:", invoice.Discount));
        if (settings.ShowTaxBreakup && !invoice.IsBillOfSupply)
        {
            sb.AppendLine(InLine("CGST:", invoice.CGST));
            sb.AppendLine(InLine("SGST:", invoice.SGST));
            sb.AppendLine(InLine("IGST:", invoice.IGST));
            if (invoice.Cess > 0)
                sb.AppendLine(InLine("Cess:", invoice.Cess));
        }
        sb.AppendLine(InLine("Round Off:", invoice.RoundOff));
        sb.AppendLine(Divider());
        sb.AppendLine(InLine("TOTAL (Rs.):", invoice.TotalAmount));
        sb.AppendLine(InLine("Paid:", $"{invoice.PaidAmount:F2} ({invoice.PaymentMode})"));
        sb.AppendLine(Divider());

        sb.AppendLine(BuildFooter(settings, invoice, withSummaryTab: false));
        return sb.ToString();
    }

    public async Task<(bool Success, string? RelativePathOrError)> CopyLogoToAppFolderAsync(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return (false, "No logo file was chosen.");

            if (!File.Exists(sourcePath))
                return (false, $"File not found: {sourcePath}");

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg"))
                return (false, "Choose a PNG or JPG image for the logo.");

            var folder = Path.Combine(AppContext.BaseDirectory, "Brand");
            Directory.CreateDirectory(folder);

            var fileName = ext == ".jpeg" ? "logo.jpg" : $"logo{ext}";
            var targetPath = Path.Combine(folder, fileName);
            File.Copy(sourcePath, targetPath, overwrite: true);

            return (true, Path.Combine("Brand", fileName));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // --------------------------- preview internals ---------------------------

    /// <summary>Last invoice with items + customer, or a dummy sample document.</summary>
    private async Task<Invoice> GetLastInvoiceOrDefaultAsync()
    {
        var invoice = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .Include(i => i.Customer)
            .OrderByDescending(i => i.Id)
            .FirstOrDefaultAsync();
        if (invoice != null) return invoice;

        return new Invoice
        {
            InvoiceNumber = "INV-20260101-0001",
            InvoiceDate = DateTime.Now,
            CustomerName = "Walk-in Customer",
            PlaceOfSupply = "07",
            SubTotal = 2456.00m,
            CGST = 122.80m,
            SGST = 122.80m,
            TotalAmount = 2701.60m,
            PaidAmount = 2701.60m,
            PaymentMode = "Cash",
            Status = "Paid",
            Items = new List<InvoiceItem>
            {
                new() { ProductName = "Basmati Rice 5kg", Quantity = 2, Rate = 890.00m, Amount = 1780.00m, Total = 2100.40m },
                new() { ProductName = "Sunflower Oil 1L", Quantity = 3, Rate = 132.00m, Amount = 396.00m, Total = 467.52m }
            }
        };
    }

    private static string BuildHeader(InvoiceTemplateSetting settings, Invoice invoice)
    {
        var sb = new StringBuilder();

        // Logo placeholder — the real bitmap is composed by the printing feature
        if (settings.ShowLogo)
        {
            var logoName = string.IsNullOrWhiteSpace(settings.LogoPath)
                ? "LOGO: not configured" : $"LOGO: {Path.GetFileName(settings.LogoPath)}";
            sb.AppendLine(Center($"[ {logoName} ]"));
        }

        // Company title / tagline, then the document type line
        if (!string.IsNullOrWhiteSpace(settings.HeaderText))
        {
            foreach (var line in settings.HeaderText.Split('\n'))
                sb.AppendLine(Center(line.TrimEnd('\r')));
        }
        sb.AppendLine(Center(TypeLabel(invoice)));

        return sb.ToString();
    }

    private static string TypeLabel(Invoice invoice) =>
        invoice.IsEstimate ? "ESTIMATE" : invoice.IsBillOfSupply ? "BILL OF SUPPLY" : "TAX INVOICE";

    private static string BuildFooter(InvoiceTemplateSetting settings, Invoice invoice, bool withSummaryTab)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(settings.FooterText))
        {
            sb.AppendLine(Divider());
            foreach (var line in settings.FooterText.Split('\n'))
                sb.AppendLine(Center(line.TrimEnd('\r')));
        }

        if (!string.IsNullOrWhiteSpace(settings.TermsText))
        {
            sb.AppendLine(Divider());
            sb.AppendLine("Terms & Conditions:");
            foreach (var line in Wrap(settings.TermsText))
                sb.AppendLine(line);
        }

        if (settings.ShowSignatureLine)
        {
            sb.AppendLine(Divider());
            var caption = string.IsNullOrWhiteSpace(settings.SignatureCaption)
                ? "Authorised Signatory" : settings.SignatureCaption.Trim();
            var sigSpace = new string(' ', Math.Max(4, Width - caption.Length - 2));
            sb.AppendLine($"{sigSpace}{new string('_', caption.Length + 2)}");
            sb.AppendLine($"{sigSpace} {caption}");
        }

        if (withSummaryTab)
        {
            // Small summary tab so header/footer-only previews still carry context
            sb.AppendLine(Divider());
            sb.AppendLine(InLine("Invoice No:", Truncate(invoice.InvoiceNumber, 23)));
            sb.AppendLine(InLine("Customer:", Truncate(invoice.Customer?.Name ?? invoice.CustomerName ?? "Walk-in Customer", 23)));
            sb.AppendLine(InLine("TOTAL (Rs.):", invoice.TotalAmount));
        }
        return sb.ToString();
    }

    /// <summary>"Label........... value" using the full 42-column width.</summary>
    private static string InLine(string label, decimal value) => InLine(label, value.ToString("F2"));

    private static string InLine(string label, string? value)
    {
        var left = PadValue(label, 18);
        var right = value ?? "-";
        if (right.Length > Width - left.Length)
            right = right[..(Width - left.Length)];
        return left + right;
    }

    private static string PadValue(string text, int width) => text.Length >= width
        ? text[..width]
        : text.PadRight(width);

    private static string Truncate(string? text, int maxLength)
    {
        text ??= "-";
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength)] + "...";
    }

    private static string Center(string text)
    {
        text = text.Trim();
        if (text.Length >= Width) return text[..Width];
        var left = (Width - text.Length) / 2;
        return text.PadLeft(left + text.Length).PadRight(Width);
    }

    private static string Divider() => new('-', Width);

    /// <summary>Wrap terms text to the print width, preserving blank-line structure.</summary>
    private static List<string> Wrap(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var t = raw.TrimEnd('\r');
            if (t.Length <= Width)
            {
                lines.Add(t);
                continue;
            }
            for (var i = 0; i < t.Length; i += Width)
                lines.Add(t.Substring(i, Math.Min(Width, t.Length - i)));
        }
        return lines;
    }

    private static string ItemsHeader()
    {
        var sb = new StringBuilder();
        sb.Append("Item".PadRight(22));
        sb.Append("Qty".PadLeft(5));
        sb.Append("Rate".PadLeft(7));
        sb.Append("Amt".PadLeft(8));
        return PadValue(sb.ToString(), Width);
    }

    private static string ItemsLine(InvoiceItem item)
    {
        var name = item.ProductName.Length <= 21
            ? item.ProductName
            : item.ProductName[..18] + "...";
        return new StringBuilder()
            .Append(name.PadRight(22))
            .Append(item.Quantity.ToString().PadLeft(5))
            .Append(item.Rate.ToString("F2").PadLeft(7))
            .Append(item.Amount.ToString("F2").PadLeft(8))
            .ToString().TrimEnd().PadRight(Width);
    }
}
