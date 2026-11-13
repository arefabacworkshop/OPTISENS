using OPTISENS.Core.Config;

namespace OPTISENS.Core.Optimization;

public static class ProbeBounds
{
    /// <summary>A 180-deg turn costs cm360/2 of travel and must fit the usable pad width.</summary>
    public static (double Lo, double Hi) Compute(double padWidthCm)
    {
        double lo = Constants.BoundsMinCm360;                        // 10
        double hi = Math.Min(Constants.BoundsMaxCm360,               // 60
                             Constants.PadTurnFraction * padWidthCm); // 2.0 * pad width
        if (hi <= lo) throw new ArgumentException(
            $"mousepad {padWidthCm} cm is too small for calibration (need >= 5 cm usable width)");
        return (lo, hi);
    }
}
