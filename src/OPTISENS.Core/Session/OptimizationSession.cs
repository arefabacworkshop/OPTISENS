using OPTISENS.Core.Config;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Scoring;

namespace OPTISENS.Core.Session;

public sealed class OptimizationSession
{
    private readonly BayesianOptimizer _opt;
    private readonly double _sessionStartS;
    private int _round; // 0-based, completed rounds

    public OptimizationSession(double padWidthCm, double sessionStartS = 0)
    {
        var (lo, hi) = ProbeBounds.Compute(padWidthCm);
        _opt = new BayesianOptimizer(lo, hi);
        _sessionStartS = sessionStartS;
    }

    public double BoundsLoCm360 => _opt.BoundsLoCm360;
    public double BoundsHiCm360 => _opt.BoundsHiCm360;
    public int CompletedRounds => _round;
    public bool CanRunRound => _round < Constants.MaxRounds; // 6
    public double NextProbeCm360() => _opt.SuggestNextProbeCm360();

    public void CompleteRound(RoundScore score)
    {
        _opt.AddObservation(score.Cm360, score.YNorm, score.RoundStartS, _sessionStartS);
        _round++;
    }

    public bool IsDone => !CanRunRound || _opt.IsConverged;
    public OptimizationReport Report() => _opt.BuildReport();
}
