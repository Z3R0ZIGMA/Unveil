using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Unveil.Core.Battery;
using static Unveil.Core.Formatting;

namespace Unveil.App.Controls;

/// <summary>
/// Full charge capacity over time, with the design capacity as a labelled reference line.
/// One series, one axis, direct labels, and a crosshair + tooltip on hover.
/// </summary>
public sealed class CapacityChart : FrameworkElement
{
    private const double Left = 64, Right = 70, Top = 14, Bottom = 30;

    private List<(DateTimeOffset Date, int Full, double? Health)> _points = [];
    private int? _design;
    private int _hover = -1;

    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
        nameof(Entries), typeof(IEnumerable<CapacityHistoryEntry>), typeof(CapacityChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((CapacityChart)d).Rebuild()));

    public static readonly DependencyProperty LineBrushProperty = Brush(nameof(LineBrush));
    public static readonly DependencyProperty GridBrushProperty = Brush(nameof(GridBrush));
    public static readonly DependencyProperty AxisBrushProperty = Brush(nameof(AxisBrush));
    public static readonly DependencyProperty LabelBrushProperty = Brush(nameof(LabelBrush));
    public static readonly DependencyProperty SurfaceBrushProperty = Brush(nameof(SurfaceBrush));

    public IEnumerable<CapacityHistoryEntry>? Entries
    {
        get => (IEnumerable<CapacityHistoryEntry>?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    /// <summary>The data line and markers.</summary>
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    /// <summary>Grid lines and tooltip border.</summary>
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>Axis labels, design line and crosshair.</summary>
    public Brush? AxisBrush { get => (Brush?)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    /// <summary>Value labels and tooltip text.</summary>
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    /// <summary>Card background: marker rings and tooltip fill.</summary>
    public Brush? SurfaceBrush { get => (Brush?)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(CapacityChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private void Rebuild()
    {
        var entries = Entries?.Where(e => e.Start is not null && e.FullChargeCapacity is > 0).OrderBy(e => e.Start).ToList() ?? [];
        _points = entries.Select(e => (e.Start!.Value, e.FullChargeCapacity!.Value, e.Health.HealthPercent)).ToList();
        _design = entries.Select(e => e.DesignCapacity).LastOrDefault(d => d is > 0);
        _hover = -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_points.Count < 2)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        var (t0, t1) = (_points[0].Date.ToUnixTimeSeconds(), _points[^1].Date.ToUnixTimeSeconds());
        var nearest = _points
            .Select((p, i) => (i, d: Math.Abs(X(p.Date, t0, t1) - x)))
            .MinBy(t => t.d).i;
        if (nearest != _hover)
        {
            _hover = nearest;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    private double PlotWidth => Math.Max(1, ActualWidth - Left - Right);

    private double PlotHeight => Math.Max(1, ActualHeight - Top - Bottom);

    private double X(DateTimeOffset t, long t0, long t1) =>
        Left + (t1 == t0 ? 0 : (t.ToUnixTimeSeconds() - t0) / (double)(t1 - t0) * PlotWidth);

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background so the whole area receives mouse moves.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_points.Count < 2 || ActualWidth < 200 || ActualHeight < 100)
        {
            return;
        }

        var values = _points.Select(p => (double)p.Full).ToList();
        if (_design is { } d)
        {
            values.Add(d);
        }

        var step = NiceStep((values.Max() - values.Min()) / 4);
        var yMin = Math.Floor(values.Min() * 0.97 / step) * step;
        var yMax = Math.Ceiling(values.Max() * 1.02 / step) * step;
        double Y(double v) => Top + (yMax - v) / (yMax - yMin) * PlotHeight;
        var (t0, t1) = (_points[0].Date.ToUnixTimeSeconds(), _points[^1].Date.ToUnixTimeSeconds());

        var gridPen = new Pen(GridBrush, 1);
        for (var y = yMin; y <= yMax + step / 2; y += step)
        {
            var py = Math.Round(Y(y)) + 0.5;
            dc.DrawLine(gridPen, new Point(Left, py), new Point(ActualWidth - Right, py));
            Text(dc, Number((long)y), AxisBrush, 11.5, new Point(Left - 8, py), TextAlignment.Right, centerY: true);
        }

        var first = _points[0];
        var mid = _points[_points.Count / 2];
        var last = _points[^1];
        Text(dc, Date(first.Date), AxisBrush, 11.5, new Point(X(first.Date, t0, t1), ActualHeight - Bottom + 10), TextAlignment.Left);
        Text(dc, Date(mid.Date), AxisBrush, 11.5, new Point(X(mid.Date, t0, t1), ActualHeight - Bottom + 10), TextAlignment.Center);
        Text(dc, Date(last.Date), AxisBrush, 11.5, new Point(X(last.Date, t0, t1), ActualHeight - Bottom + 10), TextAlignment.Right);

        if (_design is { } design)
        {
            var py = Math.Round(Y(design)) + 0.5;
            var dashed = new Pen(AxisBrush, 1.5) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawLine(dashed, new Point(Left, py), new Point(ActualWidth - Right, py));
            Text(dc, "Design", AxisBrush, 11.5, new Point(ActualWidth - Right + 6, py), TextAlignment.Left, centerY: true);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(X(first.Date, t0, t1), Y(first.Full)), false, false);
            foreach (var p in _points.Skip(1))
            {
                ctx.LineTo(new Point(X(p.Date, t0, t1), Y(p.Full)), true, true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);

        // Latest value: marker with a surface ring, labelled directly.
        var lastPoint = new Point(X(last.Date, t0, t1), Y(last.Full));
        dc.DrawEllipse(LineBrush, new Pen(SurfaceBrush, 2), lastPoint, 5, 5);
        Text(dc, Number(last.Full), LabelBrush, 12, new Point(lastPoint.X + 10, lastPoint.Y), TextAlignment.Left, centerY: true, bold: true);

        if (_hover >= 0 && _hover < _points.Count)
        {
            DrawHover(dc, _points[_hover], new Point(X(_points[_hover].Date, t0, t1), Y(_points[_hover].Full)));
        }
    }

    private void DrawHover(DrawingContext dc, (DateTimeOffset Date, int Full, double? Health) p, Point at)
    {
        dc.DrawLine(new Pen(AxisBrush, 1), new Point(at.X, Top), new Point(at.X, ActualHeight - Bottom));
        dc.DrawEllipse(LineBrush, new Pen(SurfaceBrush, 2), at, 5, 5);

        var line1 = Make(Date(p.Date), AxisBrush, 12, false);
        var line2 = Make($"{Number(p.Full)} mWh" + (p.Health is { } h ? $"  ({Percent(h)} of design)" : ""), LabelBrush, 13, true);
        var w = Math.Max(line1.Width, line2.Width) + 20;
        var h2 = line1.Height + line2.Height + 14;
        var x = at.X + 12 + w > ActualWidth ? at.X - 12 - w : at.X + 12;
        var y = Math.Clamp(at.Y - h2 - 8, 0, Math.Max(0, ActualHeight - h2));

        dc.DrawRoundedRectangle(SurfaceBrush, new Pen(GridBrush, 1), new Rect(x, y, w, h2), 6, 6);
        dc.DrawText(line1, new Point(x + 10, y + 6));
        dc.DrawText(line2, new Point(x + 10, y + 8 + line1.Height));
    }

    private void Text(DrawingContext dc, string text, Brush? brush, double size, Point at, TextAlignment align, bool centerY = false, bool bold = false)
    {
        var ft = Make(text, brush, size, bold);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(at.X, centerY ? at.Y - ft.Height / 2 : at.Y));
    }

    private FormattedText Make(string text, Brush? brush, double size, bool bold) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
        size,
        brush ?? (Brush)Application.Current.FindResource("Text"),
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static double NiceStep(double raw)
    {
        if (raw <= 0)
        {
            return 1000;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / magnitude;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * magnitude;
    }
}
