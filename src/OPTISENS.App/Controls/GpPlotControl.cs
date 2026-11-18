using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OPTISENS.App.Controls;

/// <summary>One grid point of the GP posterior: u = normalized cm/360 in [0,1], z = BSI/100.</summary>
public sealed record GPPoint(double U, double Mean, double Std);

/// <summary>One tested trial: u = normalized probe, z = observed BSI/100.</summary>
public sealed record ObservationPoint(double U, double YNorm);

public sealed class GpPlotControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<GPPoint>?> GridPredictionsProperty =
        AvaloniaProperty.Register<GpPlotControl, IReadOnlyList<GPPoint>?>(nameof(GridPredictions));

    public static readonly StyledProperty<IReadOnlyList<ObservationPoint>?> ObservationsProperty =
        AvaloniaProperty.Register<GpPlotControl, IReadOnlyList<ObservationPoint>?>(nameof(Observations));

    public static readonly StyledProperty<double> MinCm360Property =
        AvaloniaProperty.Register<GpPlotControl, double>(nameof(MinCm360), 10.0);

    public static readonly StyledProperty<double> MaxCm360Property =
        AvaloniaProperty.Register<GpPlotControl, double>(nameof(MaxCm360), 80.0);

    public static readonly StyledProperty<double?> SelectedCm360Property =
        AvaloniaProperty.Register<GpPlotControl, double?>(nameof(SelectedCm360));

    public static readonly StyledProperty<int> HighlightedObservationIndexProperty =
        AvaloniaProperty.Register<GpPlotControl, int>(nameof(HighlightedObservationIndex), -1);

    public IReadOnlyList<GPPoint>? GridPredictions
    {
        get => GetValue(GridPredictionsProperty);
        set => SetValue(GridPredictionsProperty, value);
    }

    public IReadOnlyList<ObservationPoint>? Observations
    {
        get => GetValue(ObservationsProperty);
        set => SetValue(ObservationsProperty, value);
    }

    public double MinCm360
    {
        get => GetValue(MinCm360Property);
        set => SetValue(MinCm360Property, value);
    }

    public double MaxCm360
    {
        get => GetValue(MaxCm360Property);
        set => SetValue(MaxCm360Property, value);
    }

    public double? SelectedCm360
    {
        get => GetValue(SelectedCm360Property);
        set => SetValue(SelectedCm360Property, value);
    }

    public int HighlightedObservationIndex
    {
        get => GetValue(HighlightedObservationIndexProperty);
        set => SetValue(HighlightedObservationIndexProperty, value);
    }

    public event Action<double>? PointClicked;

    private double? _hoverU;

    static GpPlotControl() =>
        AffectsRender<GpPlotControl>(
            GridPredictionsProperty,
            ObservationsProperty,
            MinCm360Property,
            MaxCm360Property,
            SelectedCm360Property,
            HighlightedObservationIndexProperty);

    public GpPlotControl()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private const double PadL = 44, PadR = 16, PadT = 16, PadB = 28;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#1C1F26"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#242832")), 1);
    private static readonly IBrush BandBrush = new SolidColorBrush(Color.Parse("#2800E5FF"));
    private static readonly IPen MeanPen = new Pen(new SolidColorBrush(Color.Parse("#FF00F0FF")), 2);
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.Parse("#44FFFFFF")), 1)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.Parse("#FFB03A")), 1.5)
        { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.Parse("#6600F0FF")), 1);
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#FFB03A"));
    private static readonly IBrush HighlightDotBrush = new SolidColorBrush(Color.Parse("#00F0FF"));
    private static readonly IPen DotPen = new Pen(new SolidColorBrush(Color.Parse("#090A0D")), 1);
    private static readonly IPen HighlightRingPen = new Pen(new SolidColorBrush(Color.Parse("#F4F6F8")), 2);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#8E97A6"));
    private static readonly IBrush TooltipBgBrush = new SolidColorBrush(Color.Parse("#E6090A0D"));
    private static readonly IPen TooltipBorderPen = new Pen(new SolidColorBrush(Color.Parse("#3B4252")), 1);
    private static readonly IBrush TooltipTextBrush = new SolidColorBrush(Color.Parse("#F4F6F8"));

    private double MapX(double u) => PadL + u * (Bounds.Width - PadL - PadR);
    private double MapY(double z) => Bounds.Height - PadB - z * (Bounds.Height - PadT - PadB);

    private double? PointToU(Point pt)
    {
        double plotW = Bounds.Width - PadL - PadR;
        if (plotW <= 1) return null;
        if (pt.X < PadL || pt.X > Bounds.Width - PadR || pt.Y < PadT || pt.Y > Bounds.Height - PadB)
            return null;
        return Math.Clamp((pt.X - PadL) / plotW, 0.0, 1.0);
    }

    private double UToCm360(double u) => MinCm360 + u * Math.Max(0.1, MaxCm360 - MinCm360);
    private double Cm360ToU(double cm) => Math.Clamp((cm - MinCm360) / Math.Max(0.1, MaxCm360 - MinCm360), 0.0, 1.0);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var u = PointToU(e.GetPosition(this));
        if (u != _hoverU)
        {
            _hoverU = u;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverU is not null)
        {
            _hoverU = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (GridPredictions is null || GridPredictions.Count < 2) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (PointToU(e.GetPosition(this)) is { } u)
        {
            double cm = Math.Round(UToCm360(u), 1);
            PointClicked?.Invoke(cm);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;

        context.DrawRectangle(BackgroundBrush, BorderPen, new Rect(0, 0, w, h), 6, 6);

        var preds = GridPredictions;
        if (preds is null || preds.Count < 2) { DrawText(context, "waiting for optimizer…", PadL, h / 2); return; }

        var band = new StreamGeometry();
        using (var gc = band.Open())
        {
            var f = preds[0];
            gc.BeginFigure(new Point(MapX(f.U), MapY(Clamp01(f.Mean + 1.96 * f.Std))), true);
            for (int i = 1; i < preds.Count; i++)
            {
                var p = preds[i];
                gc.LineTo(new Point(MapX(p.U), MapY(Clamp01(p.Mean + 1.96 * p.Std))));
            }
            for (int i = preds.Count - 1; i >= 0; i--)
            {
                var p = preds[i];
                gc.LineTo(new Point(MapX(p.U), MapY(Clamp01(p.Mean - 1.96 * p.Std))));
            }
            gc.EndFigure(true);
        }
        context.DrawGeometry(BandBrush, null, band);

        var mean = new StreamGeometry();
        using (var gc = mean.Open())
        {
            gc.BeginFigure(new Point(MapX(preds[0].U), MapY(Clamp01(preds[0].Mean))), false);
            for (int i = 1; i < preds.Count; i++)
                gc.LineTo(new Point(MapX(preds[i].U), MapY(Clamp01(preds[i].Mean))));
            gc.EndFigure(false);
        }
        context.DrawGeometry(null, MeanPen, mean);

        if (Observations is { } obs)
        {
            for (int i = 0; i < obs.Count; i++)
            {
                var o = obs[i];
                var pt = new Point(MapX(o.U), MapY(Clamp01(o.YNorm)));
                bool highlighted = i == HighlightedObservationIndex;
                context.DrawEllipse(highlighted ? HighlightDotBrush : DotBrush, DotPen, pt, highlighted ? 5.5 : 3.5, highlighted ? 5.5 : 3.5);
                if (highlighted)
                    context.DrawEllipse(null, HighlightRingPen, pt, 8, 8);
            }
        }

        int best = 0;
        for (int i = 1; i < preds.Count; i++)
            if (preds[i].Mean > preds[best].Mean) best = i;
        double bx = MapX(preds[best].U);
        context.DrawLine(GuidePen, new Point(bx, PadT), new Point(bx, h - PadB));
        DrawText(context, "x*", bx + 4, PadT + 2);

        if (SelectedCm360 is { } selCm)
        {
            double su = Cm360ToU(selCm);
            double sx = MapX(su);
            if (Math.Abs(sx - bx) > 4)
            {
                context.DrawLine(SelectedPen, new Point(sx, PadT), new Point(sx, h - PadB));
                DrawText(context, $"{selCm:F1}", sx + 4, PadT + 16);
            }
        }

        if (_hoverU is { } hu)
        {
            double hx = MapX(hu);
            context.DrawLine(HoverPen, new Point(hx, PadT), new Point(hx, h - PadB));

            var nearest = preds.MinBy(p => Math.Abs(p.U - hu))!;
            double hy = MapY(Clamp01(nearest.Mean));
            context.DrawEllipse(HighlightDotBrush, DotPen, new Point(hx, hy), 4, 4);

            double hoverCm = UToCm360(hu);
            string tip = $"{hoverCm:F1} cm/360  ·  BSI {nearest.Mean * 100:F1} ± {nearest.Std * 196:F1}";

            if (Observations is { } obsList)
            {
                for (int i = 0; i < obsList.Count; i++)
                {
                    if (Math.Abs(MapX(obsList[i].U) - hx) <= 8)
                    {
                        double obsCm = UToCm360(obsList[i].U);
                        tip = $"Round #{i + 1}: {obsCm:F1} cm/360  ·  BSI {obsList[i].YNorm * 100:F1}";
                        break;
                    }
                }
            }

            var ft = new FormattedText(tip, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 11, TooltipTextBrush);
            double boxW = ft.Width + 14;
            double boxH = ft.Height + 8;
            double boxX = Math.Clamp(hx + 10, PadL, Math.Max(PadL, w - PadR - boxW));
            double boxY = Math.Clamp(hy - boxH - 8, PadT, Math.Max(PadT, h - PadB - boxH));
            context.DrawRectangle(TooltipBgBrush, TooltipBorderPen, new Rect(boxX, boxY, boxW, boxH), 4, 4);
            context.DrawText(ft, new Point(boxX + 7, boxY + 4));
        }

        DrawText(context, $"{MinCm360:F0} cm", PadL, h - PadB + 8);
        string maxLabel = $"{MaxCm360:F0} cm";
        var maxFt = new FormattedText(maxLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, LabelBrush);
        context.DrawText(maxFt, new Point(Math.Max(PadL, w - PadR - maxFt.Width), h - PadB + 8));
        DrawText(context, "BSI/100", 4, PadT);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   Typeface.Default, 11, LabelBrush);
        context.DrawText(ft, new Point(x, y));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
