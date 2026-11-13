using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Units;

namespace OPTISENS.Core.Tests.Fixtures;

public static class SyntheticRound
{
    /// <summary>Generate synthetic 1 kHz mouse samples and attempts for testing the full pipeline.</summary>
    public static (RawMotionSample[] Samples, Attempt[] Attempts) Generate(
        double cm360, double dpi, int seed, double durationS = 2.0, double targetAzimuthDeg = 40.0)
    {
        var rng = new Random(seed);
        int n = (int)(durationS * 1000);
        var samples = new RawMotionSample[n];
        double countsPerDeg = PhysicalTurnDistance.FromCm(cm360).CountsPer360(dpi) / 360.0;

        // Minimum-jerk trajectory reaching targetAzimuthDeg by 0.5s, plus tremor proportional to 30/cm360
        double tremorAmp = 0.05 * (30.0 / cm360);
        double prevCounts = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i * 0.001;
            double tau = Math.Clamp(t / 0.5, 0, 1);
            double minJerk = 10 * Math.Pow(tau, 3) - 15 * Math.Pow(tau, 4) + 6 * Math.Pow(tau, 5);
            double deg = targetAzimuthDeg * minJerk + tremorAmp * Math.Sin(2 * Math.PI * 8.0 * t) + (rng.NextDouble() - 0.5) * 0.01;
            double totalCounts = deg * countsPerDeg;
            int dx = (int)Math.Round(totalCounts - prevCounts);
            prevCounts += dx;
            samples[i] = new RawMotionSample(i * 1000L, dx, 0, MouseButtons.None);
        }

        var attempts = new[]
        {
            new Attempt(BlockKind.Flick, 0.0, 0.65, targetAzimuthDeg, 3.0, true)
        };
        return (samples, attempts);
    }
}
