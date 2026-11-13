using OPTISENS.Core.Config;
using OPTISENS.Core.Kinematics;

namespace OPTISENS.Core.Scoring;

public readonly record struct BsiComponents(double Accuracy, double NormJerk, double EFlickAbs, double TAdjustNorm);
public readonly record struct RoundScore(double Cm360, double YNorm, BsiComponents Components, BlockMetrics[] Blocks, double RoundStartS = 0);
public readonly record struct ScoredBlock(BlockKind Kind, double WeightSeconds, double Accuracy, BlockMetrics Metrics);

public sealed class BsiScorer
{
    public const double W1 = Constants.W1, W2 = Constants.W2, W3 = Constants.W3, W4 = Constants.W4;

    public RoundScore Score(double cm360, IReadOnlyList<ScoredBlock> blocks, double roundStartS = 0)
    {
        double wSum = 0, acc = 0;
        foreach (var b in blocks) { wSum += b.WeightSeconds; acc += b.WeightSeconds * b.Accuracy; }
        double A = Math.Clamp(wSum > 0 ? acc / wSum : 0, 0, 1);

        double N = blocks.Select(b => b.Metrics.JerkDegPerS3 / Constants.JRefDegPerS3)
                         .DefaultIfEmpty(0.5).Select(v => Math.Clamp(v, 0, 1)).Max();

        var eAll = blocks.SelectMany(b => b.Metrics.FlickErrors).Select(Math.Abs).ToArray();
        double E = Math.Clamp(eAll.Length > 0 ? eAll.Average() : 0, 0, 1);

        var tAll = blocks.SelectMany(b => b.Metrics.AdjustLatencyMs).ToArray();
        double T = tAll.Length > 0
            ? Math.Clamp(tAll.Average() / Constants.TAdjustRefMs, 0, 1) // 500 ms
            : 0.5;

        var c = new BsiComponents(A, N, E, T);
        double y = Math.Clamp(W1 * A + W2 * (1 - N) + W3 * (1 - E) + W4 * (1 - T), 0, 1);
        return new RoundScore(cm360, y, c, blocks.Select(b => b.Metrics).ToArray(), roundStartS);
    }
}
