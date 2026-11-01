using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RetailBillingApp.Controls;

/// <summary>
/// Self-contained donut with a built-in amount + percentage legend. Like the bar control it derives
/// from Canvas and rebuilds on resize, so slices and legend stay in sync at any window width.
/// Handles the all-zero case (faint ring + message) and the single 100% slice (full ring, since a
/// 360° arc is degenerate and would render nothing).
/// </summary>
public class ChartDonutSeries : Canvas
{
    private static readonly FontFamily UiFont = new FontFamily("Segoe UI");

    private static readonly Brush DefaultTrack = Frozen(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush DefaultLabel = Frozen(Color.FromRgb(0x75, 0x75, 0x75));
    private static readonly Brush DefaultText = Frozen(Color.FromRgb(0x21, 0x21, 0x21));

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x19, 0x76, 0xD2), // primary
        Color.FromRgb(0x4C, 0xAF, 0x50), // success
        Color.FromRgb(0xFF, 0x98, 0x00), // warning
        Color.FromRgb(0xF4, 0x43, 0x36), // error
        Color.FromRgb(0xFF, 0xC1, 0x07), // accent
        Color.FromRgb(0x0D, 0x47, 0xA1), // primary dark
        Color.FromRgb(0x00, 0xBC, 0xD4), // cyan
        Color.FromRgb(0x9C, 0x27, 0xB0), // purple
    };

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ChartDonutSeries),
            new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty CenterTitleProperty =
        DependencyProperty.Register(nameof(CenterTitle), typeof(string), typeof(ChartDonutSeries),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty CenterValueProperty =
        DependencyProperty.Register(nameof(CenterValue), typeof(string), typeof(ChartDonutSeries),
            new PropertyMetadata(string.Empty, OnVisualChanged));

    public static readonly DependencyProperty EmptyMessageProperty =
        DependencyProperty.Register(nameof(EmptyMessage), typeof(string), typeof(ChartDonutSeries),
            new PropertyMetadata("No data", OnVisualChanged));

    public static readonly DependencyProperty IsCountProperty =
        DependencyProperty.Register(nameof(IsCount), typeof(bool), typeof(ChartDonutSeries),
            new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(ChartDonutSeries),
            new PropertyMetadata(DefaultLabel));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(ChartDonutSeries),
            new PropertyMetadata(DefaultTrack));

    public ChartDonutSeries()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Rebuild();
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string CenterTitle { get => (string)GetValue(CenterTitleProperty); set => SetValue(CenterTitleProperty, value); }
    public string CenterValue { get => (string)GetValue(CenterValueProperty); set => SetValue(CenterValueProperty, value); }
    public string EmptyMessage { get => (string)GetValue(EmptyMessageProperty); set => SetValue(EmptyMessageProperty, value); }
    public bool IsCount { get => (bool)GetValue(IsCountProperty); set => SetValue(IsCountProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ChartDonutSeries)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < 24 || h < 24) return;

        var slices = (ItemsSource?.OfType<ChartPoint>() ?? Enumerable.Empty<ChartPoint>())
            .Where(p => p.Value > 0)
            .ToList();
        double total = slices.Sum(p => p.Value);

        double legendW = Math.Min(Math.Max(96, w * 0.42), 210);
        double donutAreaW = Math.Max(40, w - legendW);
        double diameter = Math.Min(Math.Min(donutAreaW, h) - 16, 200);
        if (diameter < 40) diameter = 40;
        double cx = donutAreaW / 2, cy = h / 2;
        double rOuter = diameter / 2, rInner = rOuter * 0.62;

        if (total <= 0)
        {
            Children.Add(RingPath(cx, cy, rOuter, rInner, TrackBrush ?? DefaultTrack, null));
            CenteredMessage(EmptyMessage, w, h);
            return;
        }

        // Track ring sits behind the slices so gaps read cleanly.
        Children.Add(RingPath(cx, cy, rOuter, rInner, TrackBrush ?? DefaultTrack, null));

        double startAngle = 0;
        for (int i = 0; i < slices.Count; i++)
        {
            var p = slices[i];
            double sweep = p.Value / total * 360;
            var brush = ResolveFill(p, i);
            double pct = p.Value / total * 100;

            Path slice = sweep >= 359.99
                ? RingPath(cx, cy, rOuter, rInner, brush, null)
                : SegmentPath(cx, cy, rOuter, rInner, startAngle, startAngle + sweep, brush);

            slice.ToolTip = $"{p.Label}: {p.TooltipText ?? AmountText(p.Value)} ({pct:0.#}%)";
            slice.MouseEnter += (_, _) => { foreach (var c in Children.OfType<Path>()) c.Opacity = c == slice ? 1 : 0.55; };
            slice.MouseLeave += (_, _) => { foreach (var c in Children.OfType<Path>()) c.Opacity = 1; };
            Children.Add(slice);
            startAngle += sweep;
        }

        AddCenterText(cx, cy, diameter);
        BuildLegend(slices, total, donutAreaW + 8, h);
    }

    private void BuildLegend(List<ChartPoint> slices, double total, double x, double h)
    {
        double rowH = 24;
        double startY = Math.Max(6, (h - slices.Count * rowH) / 2);
        double swatchSize = 12;

        for (int i = 0; i < slices.Count; i++)
        {
            var p = slices[i];
            double y = startY + i * rowH;

            Children.Add(new Border
            {
                Width = swatchSize,
                Height = swatchSize,
                CornerRadius = new CornerRadius(2),
                Background = ResolveFill(p, i)
            });
            var swatch = (Border)Children[Children.Count - 1];
            Canvas.SetLeft(swatch, x);
            Canvas.SetTop(swatch, y + 2);

            double pct = p.Value / total * 100;
            var text = new TextBlock
            {
                FontSize = 11.5,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = ActualWidth - x - 4,
                ToolTip = $"{p.Label}: {p.TooltipText ?? AmountText(p.Value)} ({pct:0.#}%)"
            };
            text.Inlines.Add(new System.Windows.Documents.Run(p.Label + "  ") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run(AmountText(p.Value)) { Foreground = DefaultText });
            text.Inlines.Add(new System.Windows.Documents.Run($"  {pct:0.#}%"));
            Canvas.SetLeft(text, x + swatchSize + 7);
            Canvas.SetTop(text, y);
            Children.Add(text);
        }
    }

    private void AddCenterText(double cx, double cy, double diameter)
    {
        if (!string.IsNullOrEmpty(CenterValue))
        {
            var value = new TextBlock
            {
                Text = CenterValue,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                FontFamily = UiFont,
                Foreground = DefaultText,
                TextAlignment = TextAlignment.Center,
                Width = diameter
            };
            Canvas.SetLeft(value, cx - diameter / 2);
            Canvas.SetTop(value, cy - 15);
            Children.Add(value);
        }

        if (!string.IsNullOrEmpty(CenterTitle))
        {
            var title = new TextBlock
            {
                Text = CenterTitle,
                FontSize = 10.5,
                FontFamily = UiFont,
                Foreground = LabelBrush,
                TextAlignment = TextAlignment.Center,
                Width = diameter
            };
            Canvas.SetLeft(title, cx - diameter / 2);
            Canvas.SetTop(title, cy + 6);
            Children.Add(title);
        }
    }

    private void CenteredMessage(string message, double w, double h)
    {
        var t = new TextBlock
        {
            Text = message,
            FontSize = 12.5,
            FontFamily = UiFont,
            Foreground = LabelBrush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(1, w - 40)
        };
        Canvas.SetLeft(t, 20);
        Canvas.SetTop(t, (h - 20) / 2);
        Children.Add(t);
    }

    private string AmountText(double value) => IsCount ? ChartFormat.Count(value) : ChartFormat.MoneyCompact(value);

    private Brush ResolveFill(ChartPoint p, int index)
    {
        if (p.Fill != null) return p.Fill;
        return Frozen(Palette[index % Palette.Length]);
    }

    private static Path SegmentPath(double cx, double cy, double rOuter, double rInner, double a0, double a1, Brush fill)
    {
        double rad0 = a0 * Math.PI / 180, rad1 = a1 * Math.PI / 180;
        var p1o = Polar(cx, cy, rOuter, rad0);
        var p2o = Polar(cx, cy, rOuter, rad1);
        var p2i = Polar(cx, cy, rInner, rad1);
        var p1i = Polar(cx, cy, rInner, rad0);
        bool large = (a1 - a0) > 180;

        var figure = new PathFigure { StartPoint = p1o, IsClosed = true };
        figure.Segments.Add(new ArcSegment(p2o, new Size(rOuter, rOuter), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p2i, true));
        figure.Segments.Add(new ArcSegment(p1i, new Size(rInner, rInner), 0, large, SweepDirection.Counterclockwise, true));

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        // Thin surface-colour stroke so adjacent slices read as separate wedges.
        return new Path { Data = geo, Fill = fill, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1 };
    }

    private static Path RingPath(double cx, double cy, double rOuter, double rInner, Brush fill, Brush? stroke)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new EllipseGeometry(new Point(cx, cy), rOuter, rOuter));
        group.Children.Add(new EllipseGeometry(new Point(cx, cy), rInner, rInner));
        return new Path { Data = group, Fill = fill, Stroke = stroke, StrokeThickness = stroke == null ? 0 : 1 };
    }

    private static Point Polar(double cx, double cy, double radius, double radians)
        => new(cx + radius * Math.Sin(radians), cy - radius * Math.Cos(radians));

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
