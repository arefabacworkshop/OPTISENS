using OPTISENS.Core.Config;
using OPTISENS.Core.Input;

namespace OPTISENS.Core.Kinematics;

public readonly record struct KinematicBlock(
    double[] GridT,       // seconds, dt = 0.001
    double[] ViewX,       // degrees
    double[] ViewY,       // degrees
    double[] PhysX,       // cm
    double[] PhysY);      // cm

public readonly record struct BlockMetrics(
    double JerkDegPerS3,      // mean |j_Y|, tracking block
    double JerkCmPerS3,       // physical-space jerk (telemetry only, D21)
    double MeanSpeedDegPerS,  // diagnostic
    double[] FlickErrors,     // one E_flick per flick attempt
    double[] AdjustLatencyMs, // one T_adjust per click attempt
    double TimeOnTargetFrac,  // tracking block
    int Hits,
    int Attempts);            // flick/dot blocks

public static class Trajectory
{
    /// <summary>Irregular samples -> uniform 1 ms grid. Pure. Throws on empty input.</summary>
    public static KinematicBlock Resample(
        IReadOnlyList<RawMotionSample> samples, double dpi, double cm360, double tStartS, double tEndS)
    {
        if (samples.Count == 0) throw new ArgumentException("empty sample window", nameof(samples));
        double degPerCm = 360.0 / cm360;

        // 1) cumulative displacement in cm, absolute times in seconds
        var t = new double[samples.Count + 1];
        var sx = new double[samples.Count + 1];
        var sy = new double[samples.Count + 1];
        t[0] = samples[0].TimestampUs / 1e6;
        sx[0] = 0;
        sy[0] = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            t[i + 1] = samples[i].TimestampUs / 1e6;
            sx[i + 1] = sx[i] + samples[i].DxCounts * 2.54 / dpi;
            sy[i + 1] = sy[i] + samples[i].DyCounts * 2.54 / dpi;
        }

        // 2) uniform grid
        double dt = Constants.ResampleDtS; // 0.001
        int n = Math.Max(1, (int)Math.Floor((tEndS - tStartS) / dt) + 1);
        var g = new KinematicBlock(new double[n], new double[n], new double[n], new double[n], new double[n]);
        int p = 0;
        for (int k = 0; k < n; k++)
        {
            double tk = tStartS + k * dt;
            g.GridT[k] = tk;
            while (p < t.Length - 2 && t[p + 1] < tk) p++;
            double span = t[p + 1] - t[p];
            double u = span <= 0 ? 0 : Math.Clamp((tk - t[p]) / span, 0, 1);
            g.PhysX[k] = sx[p] + (sx[p + 1] - sx[p]) * u;
            g.PhysY[k] = sy[p] + (sy[p + 1] - sy[p]) * u;
            g.ViewX[k] = g.PhysX[k] * degPerCm;
            g.ViewY[k] = g.PhysY[k] * degPerCm;
        }
        return g;
    }

    /// <summary>Central differences along one series. Returns same-length array (one-sided at edges).</summary>
    public static double[] Diff(double[] s, double dt)
    {
        var d = new double[s.Length];
        if (s.Length <= 1) return d;
        for (int i = 0; i < s.Length; i++)
        {
            d[i] = (i, s.Length - 1 - i) switch
            {
                (0, _) => (s[1] - s[0]) / dt,
                (_, 0) => (s[^1] - s[^2]) / dt,
                _      => (s[i + 1] - s[i - 1]) / (2 * dt)
            };
        }
        return d;
    }

    public static double MeanAbsJerk(double[] path, double dt)
    {
        var v = Diff(path, dt);
        var a = Diff(v, dt);
        var j = Diff(a, dt);
        if (j.Length == 0) return 0;
        double sum = 0;
        foreach (var x in j) sum += Math.Abs(x);
        return sum / j.Length;
    }
}
