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
        var primaryView = SelectPrimaryAxis(block.ViewX, block.ViewY);
        var primaryPhys = SelectPrimaryAxis(block.PhysX, block.PhysY);

        double jerk = Trajectory.MeanAbsJerk(primaryView, dt);      // scoring variant (D21)
        double jerkPhys = Trajectory.MeanAbsJerk(primaryPhys, dt);  // telemetry only

        var eFlick = new List<double>();
        var tAdj = new List<double>();
        foreach (var at in attempts)
        {
            if (at.BlockKind == BlockKind.Flick)
                eFlick.Add(EFlick(block, at));
            if (at.ClickS is double click && at.Hit)
                tAdj.Add(TAdjustMs(block, at, click));
        }

        int hits = attempts.Count(a => a.Hit);
        return new BlockMetrics(
            jerk,
            jerkPhys,
            MeanSpeed2D(block, dt),
            eFlick.ToArray(),
            tAdj.ToArray(),
            0,
            hits,
            attempts.Count);
    }

    public static double EFlick(KinematicBlock b, Attempt at)
    {
        var axis = SelectPrimaryAxis(b.ViewX, b.ViewY);
        if (axis.Length == 0) return 0;

        double sigma = Math.Sign(at.SignedTargetAzimuthDeg);
        if (sigma == 0) sigma = 1;

        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        double endS = at.WindowEndS ?? at.ClickS ?? (at.TargetOnsetS + Constants.FlickTargetLifeS);
        int k1 = Math.Max(k0, IndexOfTime(b.GridT, endS));

        double y0 = axis[k0];
        double peak = 0.0;
        for (int k = k0; k <= k1 && k < axis.Length; k++)
            peak = Math.Max(peak, sigma * (axis[k] - y0));

        double target = Math.Abs(at.SignedTargetAzimuthDeg);
        if (target <= 1e-9) return 0;
        double e = (peak - target) / target;
        return Math.Clamp(e, Constants.EFlickClipLo, Constants.EFlickClipHi);
    }

    public static double TAdjustMs(KinematicBlock b, Attempt at, double clickS)
    {
        var vx = Trajectory.Diff(b.ViewX, Constants.ResampleDtS);
        var vy = Trajectory.Diff(b.ViewY, Constants.ResampleDtS);
        int len = Math.Min(vx.Length, vy.Length);
        if (len == 0) return 0;

        var omega = new double[len];
        for (int i = 0; i < len; i++)
            omega[i] = Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]);

        int k0 = IndexOfTime(b.GridT, at.TargetOnsetS);
        int kClick = Math.Clamp(IndexOfTime(b.GridT, clickS), k0, len - 1);
        int need = Constants.ZeroVelDwellMs; // 30 grid points at dt=0.001

        // Skip initial pre-movement reaction phase until ballistic motion begins (omega >= threshold)
        int kMoveStart = k0;
        while (kMoveStart < kClick && omega[kMoveStart] < Constants.ZeroVelThresholdDegPerS)
            kMoveStart++;

        // If movement never exceeded threshold during the window, fall back to k0
        int searchStart = kMoveStart < kClick ? kMoveStart : k0;

        int run = 0;
        for (int k = searchStart; k <= kClick; k++)
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

    private static double[] SelectPrimaryAxis(double[] x, double[] y)
    {
        double rangeX = 0, rangeY = 0;
        if (x.Length > 1)
        {
            double minX = x[0], maxX = x[0];
            for (int i = 1; i < x.Length; i++)
            {
                if (x[i] < minX) minX = x[i];
                if (x[i] > maxX) maxX = x[i];
            }
            rangeX = maxX - minX;
        }
        if (y.Length > 1)
        {
            double minY = y[0], maxY = y[0];
            for (int i = 1; i < y.Length; i++)
            {
                if (y[i] < minY) minY = y[i];
                if (y[i] > maxY) maxY = y[i];
            }
            rangeY = maxY - minY;
        }
        return rangeX >= 1e-6 || rangeX >= rangeY ? x : y;
    }

    private static double MeanSpeed2D(KinematicBlock b, double dt)
    {
        var vx = Trajectory.Diff(b.ViewX, dt);
        var vy = Trajectory.Diff(b.ViewY, dt);
        int len = Math.Min(vx.Length, vy.Length);
        if (len == 0) return 0;
        double s = 0;
        for (int i = 0; i < len; i++)
            s += Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]);
        return s / len;
    }

    private static int IndexOfTime(double[] t, double ts)
    {
        int k = (int)Math.Round((ts - t[0]) / Constants.ResampleDtS);
        return Math.Clamp(k, 0, t.Length - 1);
    }
}
