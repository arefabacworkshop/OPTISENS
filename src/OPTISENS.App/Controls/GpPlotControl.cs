using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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

    static GpPlotControl() =>
        AffectsRender<GpPlotControl>(GridPredictionsProperty, ObservationsProperty);

    private const double PadL = 44, PadR = 16, PadT = 16, PadB = 28;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#1C1F26"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#242832")), 1);
    private static readonly IBrush BandBrush = new SolidColorBrush(Color.Parse("#2800E5FF"));
    private static readonly IPen MeanPen = new Pen(new SolidColorBrush(Color.Parse("#FF00F0FF")), 2);
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.Parse("#44FFFFFF")), 1)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#FFB03A"));
    private static readonly IPen DotPen = new Pen(new SolidColorBrush(Color.Parse("#090A0D")), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#8E97A6"));

    private double MapX(double u) => PadL + u * (Bounds.Width - PadL - PadR);
    private double MapY(double z) => Bounds.Height - PadB - z * (Bounds.Height - PadT - PadB);

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
            foreach (var o in obs)
                context.DrawEllipse(DotBrush, DotPen, new Point(MapX(o.U), MapY(Clamp01(o.YNorm))), 3.5, 3.5);

        int best = 0;
        for (int i = 1; i < preds.Count; i++)
            if (preds[i].Mean > preds[best].Mean) best = i;
        double bx = MapX(preds[best].U);
        context.DrawLine(GuidePen, new Point(bx, PadT), new Point(bx, h - PadB));
        DrawText(context, "x*", bx + 4, PadT + 2);

        DrawText(context, "cm/360 →", PadL, h - PadB + 8);
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
