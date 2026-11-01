using System;
using System.Globalization;
using System.Windows.Media;

namespace RetailBillingApp.Controls;

/// <summary>One plotted value, shared by the bar/column and donut controls.</summary>
public sealed class ChartPoint
{
    public string Label { get; set; } = string.Empty;

    /// <summary>Magnitude used for geometry only; the exact figure lives in Display/Tooltip.</summary>
    public double Value { get; set; }

    /// <summary>Compact label drawn on the mark.</summary>
    public string? DisplayText { get; set; }

    /// <summary>Full exact figure for the tooltip; falls back to DisplayText.</summary>
    public string? TooltipText { get; set; }

    /// <summary>Optional second series value (e.g. invoice count overlaid on the sales trend).</summary>
    public double? SecondaryValue { get; set; }

    public string? SecondaryText { get; set; }

    /// <summary>Per-point fill; null lets the control use its default brush or palette.</summary>
    public Brush? Fill { get; set; }
}

/// <summary>Currency/number formatting for the chart labels. Indian units (K/L/Cr).</summary>
public static class ChartFormat
{
    private static readonly CultureInfo In = CultureInfo.GetCultureInfo("en-IN");

    public static string Money(decimal amount)
        => "₹" + amount.ToString("N2", In);

    public static string MoneyCompact(double amount)
    {
        double abs = Math.Abs(amount);
        string sign = amount < 0 ? "-" : string.Empty;
        if (abs >= 10_000_000) return $"{sign}₹{Trim(abs / 10_000_000)} Cr";
        if (abs >= 100_000) return $"{sign}₹{Trim(abs / 100_000)} L";
        if (abs >= 1_000) return $"{sign}₹{Trim(abs / 1_000)} K";
        return $"{sign}₹{abs.ToString("0", In)}";
    }

    public static string Count(double value)
        => $"{value.ToString("N0", In)}";

    private static string Trim(double x) => x.ToString("0.#", In);
}
