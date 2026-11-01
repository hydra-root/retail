using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RetailBillingApp.Services;

namespace RetailBillingApp.Print;

/// <summary>
/// INV-06 / INV-08: thermal-80mm fixed-width text renderer.
/// Mirrors TemplateService's 42-column convention exactly (label column 18,
/// items 22 / 5 / 7 / 8) so the counter receipt and the template preview agree.
/// Monetary values are the stored decimals formatted with F2 — no re-rounding,
/// so printed totals always equal the invoice totals.
/// </summary>
public static class InvoiceTextRenderer
{
    // 42-column grid, same shape TemplateService uses for the thermal preview.
    private const int Name = 22;
    private const int Qty = 5;
    private const int Rate = 7;
    private const int Amt = 8;

    public static string Render(InvoicePrintDocument doc, int width = 42)
    {
        var sb = new StringBuilder();

        // ---- header ----
        foreach (var line in doc.CompanyHeaderLines)
            sb.AppendLine(Center(line, width));
        sb.AppendLine(Center(doc.DocumentTitle, width));
        if (doc.HasCopyMarker)
            sb.AppendLine(Center($"COPY {doc.CurrentCopy} OF {doc.TotalCopies}", width));
        sb.AppendLine(new string('=', width));

        // ---- invoice meta + customer block ----
        sb.AppendLine(InLine("Invoice No:", Truncate(doc.InvoiceNumber, width - 18), width));
        sb.AppendLine(InLine("Date:", doc.InvoiceDateDisplay, width));
        sb.AppendLine(InLine("Payment:", $"{doc.PaymentMode}  ({doc.Status})", width));
        sb.AppendLine(InLine("Customer:", Truncate(doc.CustomerName, width - 18), width));
        if (!string.IsNullOrWhiteSpace(doc.CustomerGstin))
            sb.AppendLine(InLine("GSTIN:", doc.CustomerGstin, width));
        if (!string.IsNullOrWhiteSpace(doc.PlaceOfSupply))
            sb.AppendLine(InLine("Place of Supply:", doc.PlaceOfSupply, width));

        // ---- items table ----
        sb.AppendLine(new string('-', width));
        sb.AppendLine(ItemsHeader(width));
        if (doc.Items.Count == 0)
        {
            sb.AppendLine("No items");
        }
        else
        {
            foreach (var item in doc.Items)
                AppendItem(sb, item, width);
        }
        sb.AppendLine(new string('-', width));

        // ---- totals ----
        sb.AppendLine(InLine("Sub Total:", Money(doc.SubTotal), width));
        if (doc.Discount > 0m)
            sb.AppendLine(InLine("Discount:", "-" + Money(doc.Discount), width));

        // GST breakup only for a real tax invoice when the toggle is on.
        if (doc.ShowTaxBreakup && !doc.IsBillOfSupply)
        {
            if (doc.HasInterStateTax)
                sb.AppendLine(InLine("IGST:", Money(doc.Igst), width));
            else
            {
                sb.AppendLine(InLine("CGST:", Money(doc.Cgst), width));
                sb.AppendLine(InLine("SGST:", Money(doc.Sgst), width));
            }
            if (doc.Cess > 0m)
                sb.AppendLine(InLine("Cess:", Money(doc.Cess), width));
        }
        if (doc.RoundOff != 0m)
            sb.AppendLine(InLine("Round Off:", Money(doc.RoundOff), width));

        sb.AppendLine(new string('-', width));
        sb.AppendLine(InLine("TOTAL (Rs.):", Money(doc.TotalAmount), width));
        sb.AppendLine(InLine("Paid:", $"{Money(doc.PaidAmount)}", width));
        if (doc.BalanceDue > 0m)
            sb.AppendLine(InLine("Balance Due:", Money(doc.BalanceDue), width));
        sb.AppendLine(new string('=', width));

        // ---- footer / terms / signature ----
        if (!string.IsNullOrWhiteSpace(doc.FooterText))
        {
            sb.AppendLine(new string('-', width));
            foreach (var line in doc.FooterText!.Replace("\r", "").Split('\n'))
                sb.AppendLine(Center(line.Trim(), width));
        }

        if (!string.IsNullOrWhiteSpace(doc.TermsText))
        {
            sb.AppendLine(new string('-', width));
            sb.AppendLine("Terms & Conditions:");
            foreach (var line in Wrap(doc.TermsText!, width))
                sb.AppendLine(line);
        }

        if (doc.ShowSignatureLine)
        {
            sb.AppendLine();
            var caption = string.IsNullOrWhiteSpace(doc.SignatureCaption)
                ? "Authorised Signatory" : doc.SignatureCaption.Trim();
            var gap = new string(' ', Math.Max(4, width - caption.Length - 2));
            sb.AppendLine($"{gap}{new string('_', caption.Length + 2)}");
            sb.AppendLine($"{gap} {caption}");
        }

        return sb.ToString();
    }

    // ------------------------------- internals -------------------------------

    private static void AppendItem(StringBuilder sb, InvoicePrintRow item, int width)
    {
        // Word-wrap the product name inside the item column; the qty/rate/amount
        // cluster is printed on the LAST name line so numbers stay aligned right.
        var nameLines = Wrap(item.ProductName, Name - 1);
        if (nameLines.Count == 0) nameLines.Add("-");

        var taxTag = item.HsnCode != null ? $"  HSN:{item.HsnCode}" : "";
        if (!string.IsNullOrEmpty(taxTag)) taxTag = Truncate(taxTag, width);

        for (var i = 0; i < nameLines.Count; i++)
        {
            var isLast = i == nameLines.Count - 1;
            if (isLast)
            {
                sb.AppendLine(new StringBuilder()
                    .Append(nameLines[i].PadRight(Name))
                    .Append(item.Quantity.ToString().PadLeft(Qty))
                    .Append(Money(item.Rate).PadLeft(Rate))
                    .Append(Money(item.Amount).PadLeft(Amt))
                    .ToString().PadRight(width).TrimEnd());
                if (!string.IsNullOrEmpty(taxTag))
                    sb.AppendLine(taxTag);
            }
            else
            {
                sb.AppendLine(nameLines[i].PadRight(Name).PadRight(width).TrimEnd());
            }
        }
    }

    private static string ItemsHeader(int width) =>
        new StringBuilder()
            .Append("Item".PadRight(Name))
            .Append("Qty".PadLeft(Qty))
            .Append("Rate".PadLeft(Rate))
            .Append("Amt".PadLeft(Amt))
            .ToString().PadRight(width);

    private static string Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string InLine(string label, string value, int width)
    {
        var left = label.Length >= 18 ? label[..18] : label.PadRight(18);
        var room = width - left.Length;
        if (room <= 0) return left;
        if (value.Length > room) value = value[..room];
        return left + value;
    }

    private static string Center(string text, int width)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length >= width) return text[..width];
        var left = (width - text.Length) / 2;
        return text.PadLeft(left + text.Length).PadRight(width);
    }

    private static string Truncate(string? text, int max)
    {
        text ??= "-";
        if (max <= 0) return string.Empty;
        return text.Length <= max ? text : text[..Math.Max(0, max)];
    }

    /// <summary>Greedy word-wrap to the print width, preserving explicit line breaks.</summary>
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var segment = raw.Trim();
            if (segment.Length == 0) { lines.Add(string.Empty); continue; }

            var current = string.Empty;
            foreach (var word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length == 0)
                {
                    current = word;
                }
                else if (current.Length + 1 + word.Length <= width)
                {
                    current += " " + word;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }

                // A single word longer than the column is hard-broken.
                while (current.Length > width)
                {
                    lines.Add(current[..width]);
                    current = current[width..];
                }
            }
            if (current.Length > 0) lines.Add(current);
        }
        return lines;
    }
}
