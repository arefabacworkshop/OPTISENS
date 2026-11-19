using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace OPTISENS.App.Controls;

public enum TestGuideMode
{
    Tracking,
    Flick,
    Dot
}

/// <summary>
/// Native 60fps animated preview demonstrating how each OPTISENS calibration block works.
/// </summary>
public sealed class TestGuidePreviewControl : Control
{
    public static readonly StyledProperty<TestGuideMode> TestModeProperty =
        AvaloniaProperty.Register<TestGuidePreviewControl, TestGuideMode>(nameof(TestMode), TestGuideMode.Tracking);

    public TestGuideMode TestMode
    {
        get => GetValue(TestModeProperty);
        set => SetValue(TestModeProperty, value);
    }

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _sw = new();

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#090A0D"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#161C26")), 1);
    private static readonly IBrush TargetGlowBrush = new SolidColorBrush(Color.Parse("#3300F0FF"));
    private static readonly IBrush TargetFillBrush = new SolidColorBrush(Color.Parse("#00F0FF"));
    private static readonly IBrush TargetCoreBrush = new SolidColorBrush(Color.Parse("#E6FCFF"));
    private static readonly IBrush HitGlowBrush = new SolidColorBrush(Color.Parse("#332EE59D"));
    private static readonly IBrush HitFillBrush = new SolidColorBrush(Color.Parse("#2EE59D"));
    private static readonly IPen CrosshairWhitePen = new Pen(new SolidColorBrush(Color.Parse("#E6FFFFFF")), 2);
    private static readonly IPen CrosshairGreenPen = new Pen(new SolidColorBrush(Color.Parse("#2EE59D")), 2.2);
    private static readonly IPen TrailPen = new Pen(new SolidColorBrush(Color.Parse("#4400F0FF")), 2);
    private static readonly IBrush PillBgBrush = new SolidColorBrush(Color.Parse("#CC12151B"));
    private static readonly IPen PillBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2A3240")), 1);
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#8E97A6"));
    private static readonly Typeface MonoFace = new("Cascadia Mono, Consolas, monospace");

    static TestGuidePreviewControl()
    {
        AffectsRender<TestGuidePreviewControl>(TestModeProperty);
    }

    public TestGuidePreviewControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _sw.Restart();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _sw.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TestModeProperty)
        {
            _sw.Restart();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        ctx.FillRectangle(BgBrush, new Rect(0, 0, w, h));
        DrawArenaGrid(ctx, w, h);

        double t = _sw.Elapsed.TotalSeconds;
        switch (TestMode)
        {
            case TestGuideMode.Tracking:
                RenderTracking(ctx, w, h, t);
                DrawBadge(ctx, 12, h - 32, "DEMO LOOP · SMOOTH TRACKING", TargetFillBrush);
                DrawBadge(ctx, w - 122, h - 32, "NO CLICKING", HitFillBrush, rightAlignWidth: 110);
                break;

            case TestGuideMode.Flick:
                RenderFlick(ctx, w, h, t);
                DrawBadge(ctx, 12, h - 32, "DEMO LOOP · REFLEX FLICK", TargetFillBrush);
                DrawBadge(ctx, w - 122, h - 32, "LEFT-CLICK", HitFillBrush, rightAlignWidth: 110);
                break;

            case TestGuideMode.Dot:
                RenderDot(ctx, w, h, t);
                DrawBadge(ctx, 12, h - 32, "DEMO LOOP · MICRO-PRECISION", TargetFillBrush);
                DrawBadge(ctx, w - 122, h - 32, "LEFT-CLICK", HitFillBrush, rightAlignWidth: 110);
                break;
        }
    }

    private static void DrawArenaGrid(DrawingContext ctx, double w, double h)
    {
        const int cols = 8;
        const int rows = 5;
        for (int i = 1; i < cols; i++)
        {
            double x = w * i / cols;
            ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
        }
        for (int j = 1; j < rows; j++)
        {
            double y = h * j / rows;
            ctx.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }
    }

    private static void RenderTracking(DrawingContext ctx, double w, double h, double t)
    {
        double cx = w * 0.5;
        double cy = h * 0.48;
        double rx = w * 0.28;
        double ry = h * 0.19;
        double omega = 2.0 * Math.PI / 4.2;

        // Subtle motion trail
        Point? prev = null;
        for (int i = 14; i >= 0; i--)
        {
            double ts = t - i * 0.035;
            var p = new Point(cx + rx * Math.Sin(omega * ts), cy + ry * Math.Sin(2 * omega * ts));
            if (prev is not null)
                ctx.DrawLine(TrailPen, prev.Value, p);
            prev = p;
        }

        var target = new Point(cx + rx * Math.Sin(omega * t), cy + ry * Math.Sin(2 * omega * t));
        var crosshair = new Point(
            cx + rx * Math.Sin(omega * (t - 0.045)),
            cy + ry * Math.Sin(2 * omega * (t - 0.045)));

        DrawSphere(ctx, target, 18, isHit: true);
        DrawCrosshair(ctx, crosshair, isHit: true);
        DrawFloatingHint(ctx, target.X, target.Y - 34, "LOCKED · FOLLOW SMOOTHLY", HitFillBrush);
    }

    private static void RenderFlick(DrawingContext ctx, double w, double h, double t)
    {
        const double halfCycle = 2.0;
        double cycle = t % (halfCycle * 2);
        int flickIndex = (int)(cycle / halfCycle);
        double localT = cycle - flickIndex * halfCycle;

        var center = new Point(w * 0.5, h * 0.48);
        var target = flickIndex == 0
            ? new Point(w * 0.22, h * 0.34)
            : new Point(w * 0.78, h * 0.60);

        // 0.00..0.35: target spawns; 0.35..0.75: fast snap; 0.75..1.35: click & hit flash; 1.35..2.00: reset
        Point crosshair;
        bool isHit = false;
        double rippleProgress = -1;

        if (localT < 0.35)
        {
            crosshair = center;
        }
        else if (localT < 0.75)
        {
            double u = (localT - 0.35) / 0.40;
            double eased = 1.0 - Math.Pow(1.0 - u, 3);
            crosshair = Lerp(center, target, eased);
        }
        else if (localT < 1.35)
        {
            crosshair = target;
            isHit = true;
            rippleProgress = (localT - 0.75) / 0.60;
        }
        else
        {
            double u = (localT - 1.35) / 0.65;
            double eased = u * u * (3 - 2 * u);
            crosshair = Lerp(target, center, eased);
        }

        if (localT < 1.35)
        {
            DrawSphere(ctx, target, 17, isHit);
            if (localT < 0.45)
                DrawFloatingHint(ctx, target.X, target.Y - 34, "SNAP TO TARGET", TargetFillBrush);
        }

        if (rippleProgress >= 0)
        {
            DrawClickRipple(ctx, target, rippleProgress);
            DrawFloatingHint(ctx, target.X, target.Y - 36, "CLICK! HIT", HitFillBrush);
        }

        DrawCrosshair(ctx, crosshair, isHit);
    }

    private static void RenderDot(DrawingContext ctx, double w, double h, double t)
    {
        Point[] dots =
        [
            new(w * 0.42, h * 0.42),
            new(w * 0.56, h * 0.36),
            new(w * 0.51, h * 0.57)
        ];

        const double stepDur = 1.35;
        double cycle = t % (stepDur * dots.Length);
        int idx = Math.Min(dots.Length - 1, (int)(cycle / stepDur));
        double localT = cycle - idx * stepDur;

        var prevDot = dots[(idx + dots.Length - 1) % dots.Length];
        var currDot = dots[idx];

        Point crosshair;
        bool isHit = false;
        double ripple = -1;

        if (localT < 0.45)
        {
            double u = localT / 0.45;
            double eased = 1.0 - Math.Pow(1.0 - u, 3);
            crosshair = Lerp(prevDot, currDot, eased);
        }
        else if (localT < 1.05)
        {
            crosshair = currDot;
            isHit = true;
            ripple = (localT - 0.45) / 0.60;
        }
        else
        {
            crosshair = currDot;
        }

        for (int i = 0; i < dots.Length; i++)
        {
            bool active = i == idx;
            DrawSphere(ctx, dots[i], active ? 10 : 8, isHit && active, dimmed: !active);
        }

        if (ripple >= 0)
        {
            DrawClickRipple(ctx, currDot, ripple);
            DrawFloatingHint(ctx, currDot.X, currDot.Y - 28, "CLICK! HIT", HitFillBrush);
        }

        DrawCrosshair(ctx, crosshair, isHit);
    }

    private static void DrawSphere(DrawingContext ctx, Point p, double radius, bool isHit, bool dimmed = false)
    {
        if (!dimmed)
            ctx.DrawEllipse(isHit ? HitGlowBrush : TargetGlowBrush, null, p, radius * 1.65, radius * 1.65);

        IBrush fill = dimmed
            ? new SolidColorBrush(Color.Parse("#4400F0FF"))
            : (isHit ? HitFillBrush : TargetFillBrush);
        ctx.DrawEllipse(fill, null, p, radius, radius);

        if (!dimmed)
            ctx.DrawEllipse(TargetCoreBrush, null, new Point(p.X - radius * 0.25, p.Y - radius * 0.25), radius * 0.28, radius * 0.28);
    }

    private static void DrawClickRipple(DrawingContext ctx, Point p, double progress)
    {
        byte alpha = (byte)Math.Clamp((1.0 - progress) * 220, 0, 255);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0x2E, 0xE5, 0x9D)), 2);
        double r = 14 + progress * 28;
        ctx.DrawEllipse(null, pen, p, r, r);
    }

    private static void DrawCrosshair(DrawingContext ctx, Point p, bool isHit)
    {
        IPen pen = isHit ? CrosshairGreenPen : CrosshairWhitePen;
        IBrush dot = isHit ? HitFillBrush : TargetFillBrush;
        const double gap = 4;
        const double len = 9;

        ctx.DrawEllipse(dot, null, p, 2.2, 2.2);
        ctx.DrawLine(pen, new Point(p.X, p.Y - gap - len), new Point(p.X, p.Y - gap));
        ctx.DrawLine(pen, new Point(p.X, p.Y + gap), new Point(p.X, p.Y + gap + len));
        ctx.DrawLine(pen, new Point(p.X - gap - len, p.Y), new Point(p.X - gap, p.Y));
        ctx.DrawLine(pen, new Point(p.X + gap, p.Y), new Point(p.X + gap + len, p.Y));
    }

    private static void DrawFloatingHint(DrawingContext ctx, double x, double y, string text, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, 10.5, brush);
        ctx.DrawText(ft, new Point(x - ft.Width * 0.5, Math.Max(8, y)));
    }

    private static void DrawBadge(DrawingContext ctx, double x, double y, string text, IBrush textBrush, double? rightAlignWidth = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace, 10.5, textBrush);
        double padX = 8;
        double boxW = rightAlignWidth ?? (ft.Width + padX * 2);
        var rect = new Rect(x, y, boxW, 22);
        ctx.DrawRectangle(PillBgBrush, PillBorderPen, rect, 4, 4);
        double textX = rightAlignWidth.HasValue ? x + (boxW - ft.Width) * 0.5 : x + padX;
        ctx.DrawText(ft, new Point(textX, y + 4));
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
