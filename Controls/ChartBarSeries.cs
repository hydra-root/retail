using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RetailBillingApp.Controls;

public enum ChartOrientation
{
    Vertical,
    Horizontal
}

/// <summary>
/// Self-contained column / horizontal-bar chart drawn with WPF shapes. Derives from Canvas so it
/// composes real UIElements (Rectangles, Lines, TextBlocks) — that gives native tooltips and hover
/// for free instead of hand-rolled hit-testing. Rebuilds on every size change so it stays responsive.
/// </summary>
public class ChartBarSeries : Canvas
{
    // Label gutters are fixed pixels by nature (space reserved for text), not assumptions about
    // the data extent — the plot area is always derived from ActualWidth/Height minus these.
    private const double VLeftGutter = 60;
    private const double VBottomGutter = 28;
    private const double HLeftGutter = 112;
    private const double HRightGutter = 74;
    private const double TopPad = 14;
    private const double RightPad = 10;
    private const int GridLines = 4;

    // Default brushes are frozen so they are safe to share as dependency-property defaults.
    // Declared before the dependency properties: static initializers run in textual order.
    private static readonly Brush FallbackSecondary = Frozen(Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly Brush FallbackLabel = Frozen(Color.FromRgb(0x75, 0x75, 0x75));
    private static readonly Brush FallbackGridLine = Frozen(Color.FromRgb(0xEC, 0xEC, 0xEC));
    private static readonly Brush FallbackAxis = Frozen(Color.FromRgb(0xBD, 0xBD, 0xBD));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ChartBarSeries),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(ChartOrientation), typeof(ChartBarSeries),
            new PropertyMetadata(ChartOrientation.Vertical, OnVisualChanged));

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(ChartBarSeries),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty SecondaryBrushProperty =
        DependencyProperty.Register(nameof(SecondaryBrush), typeof(Brush), typeof(ChartBarSeries),
            new PropertyMetadata(FallbackSecondary, OnVisualChanged));

    public static readonly DependencyProperty ShowSecondaryLineProperty =
        DependencyProperty.Register(nameof(ShowSecondaryLine), typeof(bool), typeof(ChartBarSeries),
            new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty EmptyMessageProperty =
        DependencyProperty.Register(nameof(EmptyMessage), typeof(string), typeof(ChartBarSeries),
            new PropertyMetadata("No data", OnVisualChanged));

    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(ChartBarSeries),
            new PropertyMetadata(FallbackLabel));

    public static readonly DependencyProperty GridLineBrushProperty =
        DependencyProperty.Register(nameof(GridLineBrush), typeof(Brush), typeof(ChartBarSeries),
            new PropertyMetadata(FallbackGridLine));

    public static readonly DependencyProperty AxisBrushProperty =
        DependencyProperty.Register(nameof(AxisBrush), typeof(Brush), typeof(ChartBarSeries),
            new PropertyMetadata(FallbackAxis));

    private static readonly FontFamily UiFont = new FontFamily("Segoe UI");

    private readonly Dictionary<Color, Brush> _barBrushCache = new();

    public ChartBarSeries()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => Rebuild();
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ChartOrientation Orientation { get => (ChartOrientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush? SecondaryBrush { get => (Brush?)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    public bool ShowSecondaryLine { get => (bool)GetValue(ShowSecondaryLineProperty); set => SetValue(ShowSecondaryLineProperty, value); }
    public string EmptyMessage { get => (string)GetValue(EmptyMessageProperty); set => SetValue(EmptyMessageProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush? GridLineBrush { get => (Brush?)GetValue(GridLineBrushProperty); set => SetValue(GridLineBrushProperty, value); }
    public Brush? AxisBrush { get => (Brush?)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChartBarSeries)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < 24 || h < 24) return;

        var points = ItemsSource?.OfType<ChartPoint>().ToList() ?? new List<ChartPoint>();
        bool hasData = points.Count > 0 && points.Any(p => p.Value > 0);
        if (!hasData)
        {
            DrawEmptyFrame(w, h);
            return;
        }

        if (Orientation == ChartOrientation.Vertical)
            BuildVertical(points, w, h);
        else
            BuildHorizontal(points, w, h);
    }

    private void BuildVertical(List<ChartPoint> points, double w, double h)
    {
        double plotLeft = VLeftGutter, plotTop = TopPad;
        double plotW = Math.Max(1, w - VLeftGutter - RightPad);
        double plotH = Math.Max(1, h - TopPad - VBottomGutter);
        double plotBottom = plotTop + plotH, plotRight = plotLeft + plotW;

        double niceMax = NiceCeil(points.Max(p => p.Value));
        DrawYGrid(plotLeft, plotRight, plotBottom, plotH, niceMax);

        int n = points.Count;
        double slot = plotW / n;
        double barW = Math.Min(slot * 0.62, 46);
        int maxLabels = Math.Max(1, (int)(plotW / 46));
        int stride = (int)Math.Ceiling(n / (double)maxLabels);

        var secondary = ShowSecondaryLine
            ? points.Where(p => p.SecondaryValue.HasValue).Select(p => p.SecondaryValue!.Value).ToList()
            : new List<double>();
        double secMax = secondary.Count > 0 ? NiceCeil(secondary.Max()) : 0;

        var linePoints = new PointCollection();
        var dots = new List<Ellipse>();

        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            double cx = plotLeft + slot * i + slot / 2;

            double barH = niceMax > 0 ? p.Value / niceMax * plotH : 0;
            if (barH > 0.5)
            {
                double x = cx - barW / 2, yTop = plotBottom - barH;
                var rect = new Rectangle
                {
                    Width = barW,
                    Height = barH,
                    Fill = ResolveBarBrush(p),
                    ToolTip = p.TooltipText ?? p.DisplayText ?? p.Label
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, yTop);

                var hover = new TextBlock
                {
                    Text = p.DisplayText ?? ChartFormat.MoneyCompact(p.Value),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = UiFont,
                    Foreground = LabelBrush,
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                Canvas.SetLeft(hover, plotLeft + slot * i);
                Canvas.SetTop(hover, yTop > plotTop + 16 ? yTop - 16 : yTop + 3);

                rect.MouseEnter += (_, _) => { hover.Visibility = Visibility.Visible; rect.Opacity = 0.82; };
                rect.MouseLeave += (_, _) => { hover.Visibility = Visibility.Collapsed; rect.Opacity = 1; };
                Children.Add(rect);
                Children.Add(hover);
            }

            if (i % stride == 0)
            {
                var cat = new TextBlock
                {
                    Text = p.Label,
                    FontSize = 10,
                    FontFamily = UiFont,
                    Foreground = LabelBrush,
                    TextAlignment = TextAlignment.Center,
                    Width = Math.Min(slot * stride, plotW - slot * i)
                };
                Canvas.SetLeft(cat, plotLeft + slot * i);
                Canvas.SetTop(cat, plotBottom + 6);
                Children.Add(cat);
            }

            if (secMax > 0 && p.SecondaryValue.HasValue)
            {
                double sy = plotBottom - p.SecondaryValue.Value / secMax * plotH;
                linePoints.Add(new Point(cx, sy));
                dots.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = SecondaryBrush,
                    ToolTip = p.SecondaryText ?? p.SecondaryValue.Value.ToString("0")
                });
            }
        }

        if (linePoints.Count > 1)
            Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = SecondaryBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            });
        for (int i = 0; i < dots.Count && i < linePoints.Count; i++)
        {
            var dot = dots[i];
            var anchor = linePoints[i];
            Canvas.SetLeft(dot, anchor.X - 3);
            Canvas.SetTop(dot, anchor.Y - 3);
            Children.Add(dot);
        }
    }

    private void BuildHorizontal(List<ChartPoint> points, double w, double h)
    {
        double plotLeft = HLeftGutter, plotTop = TopPad;
        double plotW = Math.Max(1, w - HLeftGutter - HRightGutter);
        double plotH = Math.Max(1, h - TopPad - VBottomGutter);
        double plotBottom = plotTop + plotH, plotRight = plotLeft + plotW;

        double niceMax = NiceCeil(points.Max(p => p.Value));

        // Vertical gridlines + bottom ticks
        for (int i = 0; i <= GridLines; i++)
        {
            double t = niceMax * i / GridLines;
            double x = plotLeft + plotW * i / GridLines;
            Children.Add(new Line
            {
                X1 = x, Y1 = plotTop, X2 = x, Y2 = plotBottom,
                Stroke = i == 0 ? AxisBrush : GridLineBrush,
                StrokeThickness = i == 0 ? 1.5 : 1
            });
            var tick = new TextBlock
            {
                Text = ChartFormat.MoneyCompact(t),
                FontSize = 10,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                Width = 68,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(tick, x - 34);
            Canvas.SetTop(tick, plotBottom + 6);
            Children.Add(tick);
        }

        int n = points.Count;
        double slot = plotH / n;
        double barH = Math.Min(slot * 0.6, 32);

        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            double cy = plotTop + slot * i + slot / 2;
            double barW = niceMax > 0 ? p.Value / niceMax * plotW : 0;
            if (barW < 0.5) barW = 0.5;

            var rect = new Rectangle
            {
                Width = barW,
                Height = barH,
                Fill = ResolveBarBrush(p),
                RadiusX = 2,
                RadiusY = 2,
                ToolTip = p.TooltipText ?? p.DisplayText ?? p.Label
            };
            Canvas.SetLeft(rect, plotLeft);
            Canvas.SetTop(rect, cy - barH / 2);
            Children.Add(rect);

            var cat = new TextBlock
            {
                Text = p.Label,
                FontSize = 11.5,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                TextAlignment = TextAlignment.Right,
                Width = HLeftGutter - 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(cat, 2);
            Canvas.SetTop(cat, cy - 9);
            Children.Add(cat);

            var val = new TextBlock
            {
                Text = p.DisplayText ?? ChartFormat.MoneyCompact(p.Value),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                Width = HRightGutter - 8
            };
            Canvas.SetLeft(val, plotLeft + barW + 6);
            Canvas.SetTop(val, cy - 9);
            Children.Add(val);
        }
    }

    private void DrawYGrid(double plotLeft, double plotRight, double plotBottom, double plotH, double niceMax)
    {
        for (int i = 0; i <= GridLines; i++)
        {
            double t = niceMax * i / GridLines;
            double y = plotBottom - plotH * i / GridLines;
            Children.Add(new Line
            {
                X1 = plotLeft, Y1 = y, X2 = plotRight, Y2 = y,
                Stroke = i == 0 ? AxisBrush : GridLineBrush,
                StrokeThickness = i == 0 ? 1.5 : 1
            });
            var tick = new TextBlock
            {
                Text = ChartFormat.MoneyCompact(t),
                FontSize = 10,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                Width = plotLeft - 8,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(tick, 2);
            Canvas.SetTop(tick, y - 8);
            Children.Add(tick);
        }
    }

    private void DrawEmptyFrame(double w, double h)
    {
        Children.Add(new Rectangle
        {
            Width = Math.Max(1, w - 16),
            Height = Math.Max(1, h - 16),
            Stroke = GridLineBrush,
            StrokeThickness = 1,
            RadiusX = 6,
            RadiusY = 6
        });
        Children.Add(new TextBlock
        {
            Text = EmptyMessage,
            FontSize = 13,
            FontFamily = UiFont,
            Foreground = LabelBrush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(1, w - 48)
        });
        var last = (TextBlock)Children[Children.Count - 1];
        Canvas.SetLeft(last, 24);
        Canvas.SetTop(last, (h - 20) / 2);
    }

    /// <summary>Solid fills are upgraded to a subtle vertical gradient for a polished look.</summary>
    private Brush ResolveBarBrush(ChartPoint p)
    {
        var src = p.Fill ?? BarBrush;
        if (src == null) return DefaultBarBrush();
        if (src is not SolidColorBrush solid) return src;

        if (_barBrushCache.TryGetValue(solid.Color, out var cached)) return cached;
        var grad = new LinearGradientBrush(solid.Color, Darken(solid.Color, 0.74), 90) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        grad.Freeze();
        _barBrushCache[solid.Color] = grad;
        return grad;
    }

    private Brush DefaultBarBrush()
    {
        var top = Color.FromRgb(0x19, 0x76, 0xD2);
        var grad = new LinearGradientBrush(top, Darken(top, 0.74), 90) { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        grad.Freeze();
        return grad;
    }

    private static Color Darken(Color c, double f)
        => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Round the axis maximum up to a 1/2/2.5/5/10 × 10^n step so ticks land on clean ₹ units.</summary>
    private static double NiceCeil(double v)
    {
        if (v <= 0) return 1;
        double exp = Math.Floor(Math.Log10(v));
        double pow = Math.Pow(10, exp);
        double frac = v / pow;
        double nice = frac <= 1 ? 1 : frac <= 2 ? 2 : frac <= 2.5 ? 2.5 : frac <= 5 ? 5 : 10;
        return nice * pow;
    }
}
