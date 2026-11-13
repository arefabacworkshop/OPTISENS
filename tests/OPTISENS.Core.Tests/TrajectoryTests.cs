using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using Xunit;

namespace OPTISENS.Core.Tests;

public class TrajectoryTests
{
    [Fact]
    public void Trajectory_ConstantVelocity_ZeroJerk()
    {
        // 1000 samples, dx = 4 counts / 1 ms, DPI = 800
        // v = 4 * 2.54 / 800 / 0.001 = 12.7 cm/s
        var samples = new RawMotionSample[1001];
        samples[0] = new RawMotionSample(0L, 0, 0, MouseButtons.None);
        for (int i = 1; i <= 1000; i++)
            samples[i] = new RawMotionSample(i * 1000L, 4, 0, MouseButtons.None);

        var block = Trajectory.Resample(samples, dpi: 800, cm360: 30, tStartS: 0.0, tEndS: 1.0);
        var v = Trajectory.Diff(block.PhysX, 0.001);
        double meanV = v.Average();
        Assert.InRange(Math.Abs(meanV - 12.7), 0, 1e-9);

        double jerk = Trajectory.MeanAbsJerk(block.PhysX, 0.001);
        Assert.True(jerk < 1e-6, $"Expected jerk < 1e-6, got {jerk}");
    }

    [Fact]
    public void Trajectory_Resample_GridSpacing()
    {
        var rng = new Random(42);
        var samples = new List<RawMotionSample>();
        long ts = 0;
        for (int i = 0; i < 200; i++)
        {
            ts += (long)((0.7 + rng.NextDouble() * 1.6) * 1000); // 0.7 - 2.3 ms
            samples.Add(new RawMotionSample(ts, 2, 1, MouseButtons.None));
        }

        double tStart = samples[0].TimestampUs / 1e6;
        double tEnd = samples[^1].TimestampUs / 1e6;
        var block = Trajectory.Resample(samples, 800, 30, tStart, tEnd);

        int expectedLen = (int)Math.Floor((tEnd - tStart) / 0.001) + 1;
        Assert.Equal(expectedLen, block.GridT.Length);
        for (int k = 1; k < block.GridT.Length; k++)
            Assert.InRange(Math.Abs((block.GridT[k] - block.GridT[k - 1]) - 0.001), 0, 1e-12);
    }

    [Fact]
    public void EFlick_Overshoot02()
    {
        var block = MakeViewYBlock(100, k => k < 50 ? k * 2.4 : 120.0); // target 100, peak 120 -> E = 0.2
        var at = new Attempt(BlockKind.Flick, 0.0, 0.09, 100.0, 3.0, true);
        double e = KinematicFeatureExtractor.EFlick(block, at);
        Assert.InRange(Math.Abs(e - 0.2), 0, 1e-9);
    }

    [Fact]
    public void EFlick_Clamps()
    {
        var block = MakeViewYBlock(100, k => k * 10.0); // peak = 990, target = 100 -> raw E = 8.9 -> clamps to 2.0
        var at = new Attempt(BlockKind.Flick, 0.0, 0.09, 100.0, 3.0, true);
        double e = KinematicFeatureExtractor.EFlick(block, at);
        Assert.Equal(2.0, e);
    }

    [Fact]
    public void TAdjust_SettledClick()
    {
        // 0..1.0s moving fast (100 deg/s), from t=1.0s stationary (omega < 5 deg/s), click at 1.25s -> ~250 ms
        var block = MakeViewYBlock(1500, k => k < 1000 ? k * 0.1 : 100.0);
        var at = new Attempt(BlockKind.Flick, 0.0, 1.25, 100.0, 3.0, true);
        double tAdj = KinematicFeatureExtractor.TAdjustMs(block, at, 1.25);
        Assert.InRange(tAdj, 248.0, 252.0);
    }

    [Fact]
    public void TAdjust_NoSettle()
    {
        var block = MakeViewYBlock(500, k => k * 0.1); // constant 100 deg/s, never settles
        var at = new Attempt(BlockKind.Flick, 0.0, 0.4, 50.0, 3.0, true);
        double tAdj = KinematicFeatureExtractor.TAdjustMs(block, at, 0.4);
        Assert.Equal(0.0, tAdj);
    }

    private static KinematicBlock MakeViewYBlock(int n, Func<int, double> viewYAt)
    {
        var t = new double[n];
        var y = new double[n];
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            t[i] = i * 0.001;
            y[i] = viewYAt(i);
        }
        return new KinematicBlock(t, z, y, z, z);
    }
}
