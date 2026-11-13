using OPTISENS.Core.Config;

namespace OPTISENS.Core.Optimization;

public sealed record OptimizationReport(
    double RecommendedCm360,
    double BandLoCm360,
    double BandHiCm360,   // {x : mu(x) >= mu* - 0.02}
    double PosteriorMean,
    double PosteriorStd,
    (double Cm360, double YNorm, double NoiseVar)[] Observations,
    (double Cm360, double Mean, double Std)[] Curve); // 150 grid points, for the plot

public sealed class BayesianOptimizer
{
    private readonly double _lo, _hi; // cm360 bounds
    private readonly List<(double U, double Z, double NoiseVar)> _obs = new();
    private GpRegressor _gp = new();

    public BayesianOptimizer(double boundsLoCm360, double boundsHiCm360)
    {
        if (boundsHiCm360 <= boundsLoCm360) throw new ArgumentException("empty bounds");
        _lo = boundsLoCm360;
        _hi = boundsHiCm360;
    }

    public double BoundsLoCm360 => _lo;
    public double BoundsHiCm360 => _hi;

    public IReadOnlyList<(double Cm360, double YNorm, double NoiseVar)> Observations =>
        _obs.Select(o => (ToCm(o.U), o.Z, o.NoiseVar)).ToArray();

    /// <summary>D15: first two probes are the bounds anchors.</summary>
    public double SuggestNextProbeCm360()
    {
        if (_obs.Count == 0) return _lo;
        if (_obs.Count == 1) return _hi;
        Refit();
        double best = _obs.Max(o => o.Z);
        double bestU = 0.5, bestEi = double.NegativeInfinity;
        for (int i = 0; i < Constants.EiGridPoints; i++) // 150
        {
            double u = i / (double)(Constants.EiGridPoints - 1);
            var (m, v) = _gp.Predict(u);
            double ei = EiAcquirer.Evaluate(m, v, best, Constants.EiXi); // 0.01
            if (ei > bestEi) { bestEi = ei; bestU = u; }
        }
        return ToCm(bestU);
    }

    public void AddObservation(double cm360, double yNorm, double roundElapsedS, double sessionStartS)
    {
        double omega = Math.Exp(-Constants.FatigueLambda * (roundElapsedS - sessionStartS) / 60.0); // lambda=0.05
        double noiseVar = Constants.GpNoiseFloor / omega; // 0.05 / omega_r
        _obs.Add((FromCm(cm360), yNorm, noiseVar));
    }

    /// <summary>eps-stop (D14): max posterior std over the grid below 0.05.</summary>
    public bool IsConverged
    {
        get
        {
            if (_obs.Count < 3) return false;
            Refit();
            double maxStd = 0;
            for (int i = 0; i < Constants.EiGridPoints; i++)
            {
                var (_, v) = _gp.Predict(i / (double)(Constants.EiGridPoints - 1));
                maxStd = Math.Max(maxStd, Math.Sqrt(v));
            }
            return maxStd < Constants.ConvergenceEps; // 0.05
        }
    }

    public OptimizationReport BuildReport()
    {
        Refit();
        int n = Constants.EiGridPoints;
        var curve = new (double, double, double)[n];
        double bestMean = double.NegativeInfinity, bestU = 0.5;
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            var (m, v) = _gp.Predict(u);
            curve[i] = (ToCm(u), m, Math.Sqrt(v));
            if (m > bestMean) { bestMean = m; bestU = u; }
        }
        double bandLo = bestU, bandHi = bestU;
        foreach (var (x, m, _) in curve)
        {
            if (m >= bestMean - Constants.RecommendBandEps) // 0.02
            {
                bandLo = Math.Min(bandLo, FromCm(x));
                bandHi = Math.Max(bandHi, FromCm(x));
            }
        }
        var (_, bestVar) = _gp.Predict(bestU);
        return new OptimizationReport(
            ToCm(bestU),
            ToCm(bandLo),
            ToCm(bandHi),
            bestMean,
            Math.Sqrt(bestVar),
            _obs.Select(o => (ToCm(o.U), o.Z, o.NoiseVar)).ToArray(),
            curve);
    }

    private void Refit()
    {
        _gp = new GpRegressor();
        _gp.Fit(
            _obs.Select(o => o.U).ToArray(),
            _obs.Select(o => o.Z).ToArray(),
            _obs.Select(o => o.NoiseVar).ToArray());
    }

    private double FromCm(double x) => Math.Clamp((x - _lo) / (_hi - _lo), 0, 1);
    private double ToCm(double u) => _lo + u * (_hi - _lo);
}
