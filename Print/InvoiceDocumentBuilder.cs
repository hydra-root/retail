using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RetailBillingApp.Services;

namespace RetailBillingApp.Print;

/// <summary>
/// INV-06 / INV-08: builds the A4 / half-sheet invoice as a paginated FlowDocument.
/// Amounts come straight from the stored invoice/line decimals (formatted, never
/// re-derived), so the printed totals equal the invoice totals exactly. The thermal
/// path does not use this class. Tables are real FlowDocument Tables (they paginate);
/// the header/totals/signature use Grids hosted in BlockUIContainer.
/// </summary>
public static class InvoiceDocumentBuilder
{
    // Layout metrics in device-independent units (1/96").
    private const double A4Width = 794;    // 8.27"
    private const double A4Height = 1122;  // 11.69"
    private const double HalfWidth = 559;  // A5 ~ 5.83"
    private const double HalfHeight = 794; // A5 ~ 8.27"

    private static readonly Brush InkBrush = Frozen(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x75, 0x75, 0x75));
    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD));
    private static readonly Brush HeadBrush = Frozen(Color.FromRgb(0x19, 0x76, 0xD2));
    private static readonly Brush BandBrush = Frozen(Color.FromRgb(0xEE, 0xF2, 0xF7));
    private static readonly Brush EstimateBrush = Frozen(Color.FromRgb(0xF4, 0x43, 0x36));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static FlowDocument Build(InvoicePrintDocument doc)
    {
        var isHalf = doc.Layout == InvoicePrintLayout.HalfSheet;
        var pageWidth = isHalf ? HalfWidth : A4Width;
        var pageHeight = isHalf ? HalfHeight : A4Height;
        var baseSize = isHalf ? 11.0 : 12.5;

        var fd = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            ColumnWidth = pageWidth,      // single column -> correct pagination
            PagePadding = new Thickness(isHalf ? 28 : 40, isHalf ? 24 : 36, isHalf ? 28 : 40, isHalf ? 24 : 36),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = baseSize,
            Foreground = InkBrush
        };

        if (doc.IsEstimate)
            fd.Blocks.Add(BuildEstimateBanner(baseSize + 4));

        fd.Blocks.Add(BuildHeaderBlock(doc, baseSize));
        fd.Blocks.Add(BuildMetaTable(doc, baseSize));
        fd.Blocks.Add(BuildItemsTable(doc, baseSize));
        fd.Blocks.Add(BuildTotalsBlock(doc, baseSize));
        if (doc.ShowTaxBreakup && !doc.IsBillOfSupply)
        {
            foreach (var b in BuildTaxBreakup(doc, baseSize))
                fd.Blocks.Add(b);
        }

        foreach (var b in BuildFooterBlocks(doc, baseSize))
            fd.Blocks.Add(b);

        return fd;
    }

    // ------------------------------- header -------------------------------

    private static Block BuildEstimateBanner(double size)
    {
        return new Paragraph(new Run("E S T I M A T E   —   N O T   A   T A X   I N V O I C E"))
        {
            TextAlignment = TextAlignment.Center,
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = EstimateBrush,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    private static Block BuildHeaderBlock(InvoicePrintDocument doc, double baseSize)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var logo = TryLoadLogo(doc.LogoAbsolutePath);
        if (logo != null)
        {
            stack.Children.Add(new Image
            {
                Source = logo,
                MaxHeight = 64,
                MaxWidth = 220,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        for (var i = 0; i < doc.CompanyHeaderLines.Count; i++)
        {
            var line = doc.CompanyHeaderLines[i];
            stack.Children.Add(new TextBlock
            {
                Text = line,
                TextAlignment = TextAlignment.Center,
                FontSize = i == 0 ? baseSize + 6 : baseSize,
                FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                Foreground = i == 0 ? HeadBrush : InkBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = doc.DocumentTitle,
            TextAlignment = TextAlignment.Center,
            FontSize = baseSize + 1,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 2, 0, 0)
        });

        if (doc.HasCopyMarker)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"COPY {doc.CurrentCopy} OF {doc.TotalCopies}",
                TextAlignment = TextAlignment.Center,
                FontSize = baseSize - 1,
                FontWeight = FontWeights.Bold,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        return new BlockUIContainer(stack) { Margin = new Thickness(0, 0, 0, 10) };
    }

    private static ImageSource? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // no file lock left behind
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path!, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // a missing / unreadable logo never blocks the print job
        }
    }

    // ---------------------------- meta + customer ----------------------------

    private static Block BuildMetaTable(InvoicePrintDocument doc, double baseSize)
    {
        var table = NewTable(2);
        table.Columns[0].Width = new GridLength(1, GridUnitType.Star);
        table.Columns[1].Width = new GridLength(1, GridUnitType.Star);

        var group = new TableRowGroup();
        group.Rows.Add(MetaRow(Pair("Invoice No:", doc.InvoiceNumber), Pair("Date:", doc.InvoiceDateDisplay), baseSize));
        group.Rows.Add(MetaRow(Pair("Payment Mode:", doc.PaymentMode), Pair("Status:", doc.Status), baseSize));
        group.Rows.Add(MetaRow(Pair("Customer:", doc.CustomerName), Pair("Place of Supply:", dashIfBlank(doc.PlaceOfSupply)), baseSize));
        group.Rows.Add(MetaRow(Pair("GSTIN:", dashIfBlank(doc.CustomerGstin)), Pair("Type:", doc.DocumentTitle), baseSize));
        if (doc.HasCopyMarker)
            group.Rows.Add(MetaRow(Pair($"Copy:", $"{doc.CurrentCopy} of {doc.TotalCopies}"), ("", ""), baseSize));
        table.RowGroups.Add(group);

        return table;
    }

    private static TableRow MetaRow((string, string) left, (string, string) right, double size)
    {
        var row = new TableRow();
        row.Cells.Add(MetaCell(left, size));
        row.Cells.Add(MetaCell(right, size));
        return row;
    }

    private static TableCell MetaCell((string label, string value) pair, double size)
    {
        var p = new Paragraph { Margin = new Thickness(0), FontSize = size };
        if (!string.IsNullOrEmpty(pair.label))
        {
            p.Inlines.Add(new Run(pair.label + " ") { Foreground = MutedBrush });
            p.Inlines.Add(new Run(pair.value) { FontWeight = FontWeights.SemiBold });
        }
        return new TableCell(p) { Padding = new Thickness(2, 1, 10, 1), BorderThickness = new Thickness(0) };
    }

    private static (string, string) Pair(string label, string? value) => (label, value ?? "-");

    // ------------------------------ items table ------------------------------

    private static Block BuildItemsTable(InvoicePrintDocument doc, double baseSize)
    {
        var showTaxCols = doc.ShowTaxBreakup && !doc.IsBillOfSupply;
        var cols = showTaxCols ? 8 : 6;
        var table = NewTable(cols);

        // # | Product/HSN | Qty | Rate | [GST%] | Amount | [Tax] | Total
        table.Columns[0].Width = GridLength.Auto;
        table.Columns[1].Width = new GridLength(1, GridUnitType.Star);
        for (var i = 2; i < cols; i++) table.Columns[i].Width = GridLength.Auto;

        var head = new TableRowGroup { Background = BandBrush };
        var hr = new TableRow();
        hr.Cells.Add(HeadCell("#", baseSize, TextAlignment.Center));
        hr.Cells.Add(HeadCell("Item / HSN", baseSize, TextAlignment.Left));
        hr.Cells.Add(HeadCell("Qty", baseSize, TextAlignment.Right));
        hr.Cells.Add(HeadCell("Rate", baseSize, TextAlignment.Right));
        if (showTaxCols) hr.Cells.Add(HeadCell("GST%", baseSize, TextAlignment.Right));
        hr.Cells.Add(HeadCell("Amount", baseSize, TextAlignment.Right));
        if (showTaxCols) hr.Cells.Add(HeadCell("Tax", baseSize, TextAlignment.Right));
        hr.Cells.Add(HeadCell("Total", baseSize, TextAlignment.Right));
        head.Rows.Add(hr);
        table.RowGroups.Add(head);

        var body = new TableRowGroup();
        if (doc.Items.Count == 0)
        {
            var empty = new TableRow();
            empty.Cells.Add(new TableCell(
                P(new Run("No items on this document") { FontStyle = FontStyles.Italic, Foreground = MutedBrush }, baseSize, TextAlignment.Center))
            { ColumnSpan = cols, Padding = new Thickness(6, 8, 6, 8), BorderThickness = new Thickness(0) });
            body.Rows.Add(empty);
        }
        else
        {
            foreach (var item in doc.Items)
                body.Rows.Add(ItemsBodyRow(item, showTaxCols, baseSize));
        }
        table.RowGroups.Add(body);

        return table;
    }

    private static TableRow ItemsBodyRow(InvoicePrintRow item, bool showTaxCols, double size)
    {
        var row = new TableRow();
        row.Cells.Add(BodyCell(P(new Run(item.Serial.ToString()), size, TextAlignment.Center)));
        row.Cells.Add(BodyCell(ProductCell(item, size)));
        row.Cells.Add(BodyCell(P(new Run(item.Quantity.ToString()), size, TextAlignment.Right)));
        row.Cells.Add(BodyCell(P(new Run(Money(item.Rate)), size, TextAlignment.Right)));
        if (showTaxCols) row.Cells.Add(BodyCell(P(new Run(TaxLabel(item)), size, TextAlignment.Right)));
        row.Cells.Add(BodyCell(P(new Run(Money(item.Amount)), size, TextAlignment.Right)));
        if (showTaxCols) row.Cells.Add(BodyCell(P(new Run(Money(item.DisplayTax)), size, TextAlignment.Right)));
        row.Cells.Add(BodyCell(P(new Run(Money(item.Total)) { FontWeight = FontWeights.SemiBold }, size, TextAlignment.Right)));
        return row;
    }

    private static Paragraph ProductCell(InvoicePrintRow item, double size)
    {
        var p = new Paragraph { Margin = new Thickness(0), FontSize = size, TextAlignment = TextAlignment.Left };
        p.Inlines.Add(new Run(item.ProductName) { FontWeight = FontWeights.SemiBold });
        if (item.TaxInclusive)
            p.Inlines.Add(new Run("  (incl. tax)") { Foreground = MutedBrush, FontSize = size - 1.5 });
        if (!string.IsNullOrWhiteSpace(item.HsnCode))
        {
            p.Inlines.Add(new LineBreak());
            p.Inlines.Add(new Run($"HSN: {item.HsnCode}") { Foreground = MutedBrush, FontSize = size - 1.5 });
        }
        return p;
    }

    private static string TaxLabel(InvoicePrintRow item)
    {
        var rate = item.TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture);
        if (item.Cess > 0m) return rate == "0" ? "+C" : $"{rate}%+C";
        return rate == "0" ? "-" : $"{rate}%";
    }

    // ------------------------------ totals block ------------------------------

    private static Block BuildTotalsBlock(InvoicePrintDocument doc, double baseSize)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (!string.IsNullOrWhiteSpace(doc.FooterText))
        {
            foreach (var line in doc.FooterText!.Replace("\r", "").Split('\n'))
                left.Children.Add(new TextBlock { Text = line.Trim(), FontSize = baseSize - 1, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap });
        }

        var totals = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        totals.Children.Add(TwoCol("Sub Total", Money(doc.SubTotal), baseSize));
        if (doc.Discount > 0m) totals.Children.Add(TwoCol("Discount", "-" + Money(doc.Discount), baseSize));
        if (!doc.IsBillOfSupply && doc.ShowTaxBreakup)
        {
            if (doc.HasInterStateTax) totals.Children.Add(TwoCol("IGST", Money(doc.Igst), baseSize));
            else
            {
                totals.Children.Add(TwoCol("CGST", Money(doc.Cgst), baseSize));
                totals.Children.Add(TwoCol("SGST", Money(doc.Sgst), baseSize));
            }
            if (doc.Cess > 0m) totals.Children.Add(TwoCol("Cess", Money(doc.Cess), baseSize));
        }
        if (doc.RoundOff != 0m) totals.Children.Add(TwoCol("Round Off", Money(doc.RoundOff), baseSize));

        // Emphasised TOTAL band using the stored TotalAmount verbatim.
        totals.Children.Add(new Border
        {
            Background = BandBrush,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 4, 0, 0),
            Child = TwoCol(doc.IsBillOfSupply ? "TOTAL" : "TOTAL (incl. tax)", Money(doc.TotalAmount),
                baseSize + 1, bold: true, color: HeadBrush)
        });

        totals.Children.Add(TwoCol($"Paid ({doc.PaymentMode})", Money(doc.PaidAmount), baseSize));
        if (doc.WriteOffAmount > 0m) totals.Children.Add(TwoCol("Written Off", Money(doc.WriteOffAmount), baseSize));
        totals.Children.Add(TwoCol("Balance Due", Money(doc.BalanceDue), baseSize, bold: true));

        Grid.SetColumn(left, 0);
        Grid.SetColumn(totals, 1);
        grid.Children.Add(left);
        grid.Children.Add(totals);

        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }

    // --------------------------- tax breakup table ---------------------------

    private static IEnumerable<Block> BuildTaxBreakup(InvoicePrintDocument doc, double baseSize)
    {
        // Group lines by tax rate (rate -> taxable, tax). Display-only summary; the
        // invoice-level totals above are the authoritative figures.
        var groups = new SortedDictionary<decimal, (decimal taxable, decimal tax)>();
        foreach (var item in doc.Items)
        {
            groups.TryGetValue(item.TaxPercentage, out var g);
            g.taxable += item.Amount;
            g.tax += item.DisplayTax;
            groups[item.TaxPercentage] = g;
        }
        if (groups.Count == 0) yield break;

        yield return new Paragraph(new Run("Tax Breakup") { FontWeight = FontWeights.Bold, Foreground = HeadBrush, FontSize = baseSize + 1 })
        { Margin = new Thickness(0, 8, 0, 2) };

        var table = NewTable(3);
        table.Columns[0].Width = new GridLength(1, GridUnitType.Star);
        table.Columns[1].Width = new GridLength(1, GridUnitType.Star);
        table.Columns[2].Width = new GridLength(1, GridUnitType.Star);

        var head = new TableRowGroup { Background = BandBrush };
        var hr = new TableRow();
        hr.Cells.Add(HeadCell("GST Rate", baseSize, TextAlignment.Left));
        hr.Cells.Add(HeadCell("Taxable", baseSize, TextAlignment.Right));
        hr.Cells.Add(HeadCell(doc.HasInterStateTax ? "IGST" : "CGST + SGST", baseSize, TextAlignment.Right));
        head.Rows.Add(hr);
        table.RowGroups.Add(head);

        var body = new TableRowGroup();
        foreach (var kv in groups)
        {
            var row = new TableRow();
            row.Cells.Add(BodyCell(P(new Run($"{kv.Key:0.##}%"), baseSize, TextAlignment.Left)));
            row.Cells.Add(BodyCell(P(new Run(Money(kv.Value.taxable)), baseSize, TextAlignment.Right)));
            row.Cells.Add(BodyCell(P(new Run(Money(kv.Value.tax)), baseSize, TextAlignment.Right)));
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);

        yield return table;
    }

    private sealed class DecimalOrder : IComparer<decimal>
    {
        public int Compare(decimal x, decimal y) => x.CompareTo(y);
    }

    // ------------------------------ footer blocks ------------------------------

    private static IEnumerable<Block> BuildFooterBlocks(InvoicePrintDocument doc, double baseSize)
    {
        if (!string.IsNullOrWhiteSpace(doc.TermsText))
        {
            var terms = new Paragraph { Margin = new Thickness(0, 8, 0, 2), FontSize = baseSize, Foreground = MutedBrush };
            terms.Inlines.Add(new Run("Terms & Conditions:  ") { FontWeight = FontWeights.Bold });
            terms.Inlines.Add(new Run(doc.TermsText!.Replace("\n", "  ").Replace("\r", "")));
            yield return terms;
        }

        if (doc.ShowSignatureLine)
        {
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 34, 0, 0) };
            box.Children.Add(new Border { BorderBrush = InkBrush, BorderThickness = new Thickness(0, 0, 0, 1), Width = 180 });
            box.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(doc.SignatureCaption) ? "Authorised Signatory" : doc.SignatureCaption.Trim(),
                FontSize = baseSize,
                Foreground = MutedBrush,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            yield return new BlockUIContainer(box) { Margin = new Thickness(0) };
        }
    }

    // ------------------------------- shared helpers -------------------------------

    private static Table NewTable(int columns)
    {
        var table = new Table { CellSpacing = 0 };
        for (var i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
        return table;
    }

    private static Paragraph P(Inline inline, double size, TextAlignment align) =>
        new Paragraph(inline) { Margin = new Thickness(0), FontSize = size, TextAlignment = align };

    private static TableCell HeadCell(string text, double size, TextAlignment align) =>
        new TableCell(new Paragraph(new Run(text) { FontWeight = FontWeights.Bold })
        {
            Margin = new Thickness(0),
            FontSize = size,
            TextAlignment = align
        })
        {
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.5)
        };

    private static TableCell BodyCell(Paragraph p) =>
        new TableCell(p)
        {
            Padding = new Thickness(6, 3, 6, 3),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.4)
        };

    private static UIElement TwoCol(string label, string value, double size, bool bold = false, Brush? color = null)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new TextBlock
        {
            Text = label,
            FontSize = size,
            Foreground = color ?? MutedBrush,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
        };
        Grid.SetColumn(l, 0);
        var v = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = color ?? InkBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        return g;
    }

    private static string dashIfBlank(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();

    // Rupee-formatted amounts from the stored decimals — grouping only, no re-rounding.
    private static string Money(decimal value) => "₹" + value.ToString("#,##0.00", Inr);

    private static readonly CultureInfo Inr = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        try
        {
            return (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
