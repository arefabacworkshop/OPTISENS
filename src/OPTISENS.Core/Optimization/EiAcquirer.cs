namespace OPTISENS.Core.Optimization;

/// <summary>Expected Improvement over the incumbent best z+.</summary>
public static class EiAcquirer
{
    public static double Evaluate(double mean, double var, double best, double xi)
    {
        double sigma = Math.Sqrt(var);
        if (sigma < 1e-12) return Math.Max(0, mean - best - xi);
        double z = (mean - best - xi) / sigma;
        return (mean - best - xi) * NormCdf(z) + sigma * NormPdf(z);
    }

    public static double NormCdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    public static double NormPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

    /// <summary>Abramowitz-Stegun 7.1.26, |err| <= 1.5e-7.</summary>
    public static double Erf(double x)
    {
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
                         * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }
}
