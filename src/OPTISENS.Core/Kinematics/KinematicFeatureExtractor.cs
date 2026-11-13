using OPTISENS.Core.Config;

namespace OPTISENS.Core.Kinematics;

public sealed class KinematicFeatureExtractor
{
    /// <summary>Attempts = list of (targetOnsetS, clickS-or-null, signedTargetAzimuthDeg, blockKind).</summary>
    public BlockMetrics Extract(
        KinematicBlock block,
        IReadOnlyList<Attempt> attempts,
        double dt = Constants.ResampleDtS)
    {
        double jerk = Trajectory.MeanAbsJerk(block.ViewY, dt);      // scoring variant (D21)
        double jerkPhys = Trajectory.MeanAbsJerk(block.PhysY, dt);  // telemetry only

        var eFlick = new List<double>();
        var tAdj = new List<double>();
        foreach (var at in attempts)
        {
            if (at.BlockKind == BlockKind.Flick)
                eFlick.Add(EFlick(block, at));
            if (at.ClickS is double click)
                tAdj.Add(TAdjustMs(block, at, click));
        }

        int hits = attempts.Count(a => a.Hit);
        return new BlockMetrics(
            jerk,
            jerkPhys,
            MeanSpeed(block, dt),
            eFlick.ToArray(),
            tAdj.ToArray(),
            0,
            hits,
            attempts.Count);
    }

    public static double EFlick(KinematicBlock b, Attempt at)
    {
        double sigma = Math.Sign(at.SignedTargetAzimuthDeg);
        if (sigma == 0) sigma = 1;
        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        double y0 = b.ViewY[k0];
        double peak = double.MinValue;
        for (int k = k0; k < b.GridT.Length; k++)
            peak = Math.Max(peak, sigma * (b.ViewY[k] - y0));
        double target = Math.Abs(at.SignedTargetAzimuthDeg);
        if (target <= 1e-9) return 0;
        double e = (peak - target) / target;
        return Math.Clamp(e, Constants.EFlickClipLo, Constants.EFlickClipHi);
    }

    public static double TAdjustMs(KinematicBlock b, Attempt at, double clickS)
    {
        var v = Trajectory.Diff(b.ViewY, Constants.ResampleDtS);
        var omega = new double[v.Length];
        for (int i = 0; i < v.Length; i++) omega[i] = Math.Abs(v[i]);

        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        int need = Constants.ZeroVelDwellMs; // 30 grid points at dt=0.001
        int run = 0;
        for (int k = k0; k < omega.Length; k++)
        {
            run = omega[k] < Constants.ZeroVelThresholdDegPerS ? run + 1 : 0; // 5.0 deg/s
            if (run >= need)
            {
                double tZero = b.GridT[k - need + 1];
                return Math.Max(0, (clickS - tZero) * 1000.0);
            }
        }
        return 0; // never settled: latency unknown, accuracy penalizes
    }

    private static double MeanSpeed(KinematicBlock b, double dt)
    {
        var v = Trajectory.Diff(b.ViewY, dt);
        if (v.Length == 0) return 0;
        double s = 0;
        foreach (var x in v) s += Math.Abs(x);
        return s / v.Length;
    }

    private static int IndexOfTime(double[] t, double ts)
    {
        int k = (int)Math.Round((ts - t[0]) / Constants.ResampleDtS);
        return Math.Clamp(k, 0, t.Length - 1);
    }
}
