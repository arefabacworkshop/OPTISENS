namespace OPTISENS.Core.Session;

public enum TrackingPattern { HorizonStrafe, FigureEightFlow, DiagonalGlide, ReactiveWeave, PendulumArc, ApexComposite }
public enum FlickPattern { AlternatingHorizontal, DiagonalCross, CenterReturnHub, CompassStar, DoubleStepCascade, DynamicMix }
public enum DotPattern { PrecisionScatter, RingCluster, HeadLevelLine, CrosshairDiamond, AscendingArc, FinaleCluster }

/// <summary>
/// A visually distinct drill profile for one round. Target radii, timing, travel
/// envelopes and scoring constants are shared with every other variant.
/// </summary>
public sealed record RoundVariant(
    int RoundNumber,
    string Title,
    string Subtitle,
    TrackingPattern Tracking,
    FlickPattern Flick,
    DotPattern Dot,
    float AccentR,
    float AccentG,
    float AccentB)
{
    public static RoundVariant ForRound(int roundNumber) => ((Math.Max(1, roundNumber) - 1) % 6) switch
    {
        0 => new(1, "HORIZON", "Strafe · Lateral Snaps · Scatter",
            TrackingPattern.HorizonStrafe, FlickPattern.AlternatingHorizontal, DotPattern.PrecisionScatter, 0.00f, 0.90f, 1.00f),
        1 => new(2, "FLOW", "Figure-8 · Diagonal Cross · Ring",
            TrackingPattern.FigureEightFlow, FlickPattern.DiagonalCross, DotPattern.RingCluster, 0.22f, 0.74f, 0.97f),
        2 => new(3, "ORBIT", "Diagonal Glide · Hub Return · Line",
            TrackingPattern.DiagonalGlide, FlickPattern.CenterReturnHub, DotPattern.HeadLevelLine, 0.18f, 0.90f, 0.62f),
        3 => new(4, "WEAVE", "Reactive Weave · Star · Diamond",
            TrackingPattern.ReactiveWeave, FlickPattern.CompassStar, DotPattern.CrosshairDiamond, 0.98f, 0.75f, 0.22f),
        4 => new(5, "PENDULUM", "Arc Sweep · Cascade · Ascending Arc",
            TrackingPattern.PendulumArc, FlickPattern.DoubleStepCascade, DotPattern.AscendingArc, 0.66f, 0.55f, 0.98f),
        _ => new(6, "APEX", "Composite · Dynamic Mix · Finale",
            TrackingPattern.ApexComposite, FlickPattern.DynamicMix, DotPattern.FinaleCluster, 0.92f, 0.95f, 1.00f),
    };
}
