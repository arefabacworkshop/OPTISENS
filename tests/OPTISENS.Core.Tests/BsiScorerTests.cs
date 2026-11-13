using OPTISENS.Core.Config;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Scoring;
using Xunit;

namespace OPTISENS.Core.Tests;

public class BsiScorerTests
{
    private readonly BsiScorer _scorer = new();

    [Fact]
    public void Bsi_Perfect()
    {
        var metrics = new BlockMetrics(0, 0, 0, [0.0], [0.0], 1.0, 1, 1);
        var blocks = new[] { new ScoredBlock(BlockKind.Flick, 15.0, 1.0, metrics) };
        var score = _scorer.Score(30.0, blocks);
        Assert.Equal(1.0, score.YNorm);
    }

    [Fact]
    public void Bsi_Midpoint()
    {
        var metrics = new BlockMetrics(0.5 * Constants.JRefDegPerS3, 0, 0, [0.5], [0.5 * Constants.TAdjustRefMs], 0.5, 1, 2);
        var blocks = new[] { new ScoredBlock(BlockKind.Flick, 15.0, 0.5, metrics) };
        var score = _scorer.Score(30.0, blocks);
        Assert.InRange(Math.Abs(score.YNorm - 0.5), 0, 1e-12);
    }

    [Fact]
    public void Bsi_ClampsNegative()
    {
        var metrics = new BlockMetrics(Constants.JRefDegPerS3, 0, 0, [1.0], [Constants.TAdjustRefMs], 0.0, 0, 1);
        var blocks = new[] { new ScoredBlock(BlockKind.Flick, 15.0, 0.0, metrics) };
        var score = _scorer.Score(30.0, blocks);
        Assert.Equal(0.0, score.YNorm);
    }
}
