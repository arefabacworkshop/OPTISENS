using OPTISENS.Core.Optimization;
using Xunit;

namespace OPTISENS.Core.Tests;

public class OptimizerTests
{
    [Fact]
    public void Optimizer_AnchorOrder()
    {
        var opt = new BayesianOptimizer(10.0, 60.0);
        Assert.Equal(10.0, opt.SuggestNextProbeCm360());
        opt.AddObservation(10.0, 0.4, 0.0, 0.0);
        Assert.Equal(60.0, opt.SuggestNextProbeCm360());
    }

    [Fact]
    public void Optimizer_SyntheticLandscape()
    {
        var rng = new Random(1234);
        var opt = new BayesianOptimizer(10.0, 60.0);
        for (int r = 0; r < 6 && !opt.IsConverged; r++)
        {
            double x = opt.SuggestNextProbeCm360();
            double noise = (rng.NextDouble() * 2 - 1) * 0.02;
            double y = Math.Clamp(0.9 - 0.005 * Math.Pow(x - 28.0, 2) + noise, 0.0, 1.0);
            opt.AddObservation(x, y, r * 50.0, 0.0);
        }

        var report = opt.BuildReport();
        Assert.InRange(report.RecommendedCm360, 26.0, 30.0);
        Assert.True(28.0 >= report.BandLoCm360 && 28.0 <= report.BandHiCm360,
            $"Expected 28.0 in band [{report.BandLoCm360:F2}, {report.BandHiCm360:F2}]");
    }

    [Fact]
    public void Optimizer_PadBound()
    {
        var (lo1, hi1) = ProbeBounds.Compute(30.0);
        Assert.Equal(10.0, lo1);
        Assert.Equal(60.0, hi1);

        var (lo2, hi2) = ProbeBounds.Compute(12.0);
        Assert.Equal(10.0, lo2);
        Assert.Equal(24.0, hi2);
    }
}
