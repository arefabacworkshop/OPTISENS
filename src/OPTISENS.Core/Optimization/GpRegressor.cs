using OPTISENS.Core.Config;

namespace OPTISENS.Core.Optimization;

/// <summary>Exact GP regression, dense Cholesky. n <= 8 -> O(n^3) is microseconds.</summary>
public sealed class GpRegressor
{
    private double[] _x = Array.Empty<double>();
    private double[] _z = Array.Empty<double>();
    private double[] _alpha = Array.Empty<double>();
    private double[,] _L = new double[0, 0];

    public int Count => _x.Length;

    public void Fit(IReadOnlyList<double> u, IReadOnlyList<double> z, IReadOnlyList<double> noiseVar)
    {
        int n = u.Count;
        _x = u.ToArray();
        _z = z.ToArray();
        var k = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                k[i, j] = Kernel(_x[i], _x[j]) + (i == j ? noiseVar[i] : 0);
        _L = Cholesky(k);                        // lower triangular
        _alpha = SolveLt(_L, _z);                // forward
        _alpha = SolveUt(Transpose(_L), _alpha); // back
    }

    public (double Mean, double Var) Predict(double u)
    {
        if (_x.Length == 0) return (0.5, 1.0);   // prior
        int n = _x.Length;
        var ks = new double[n];
        for (int i = 0; i < n; i++) ks[i] = Kernel(_x[i], u);
        double mean = 0;
        for (int i = 0; i < n; i++) mean += ks[i] * _alpha[i];
        var v = SolveLt(_L, ks);
        double var = Kernel(u, u);
        for (int i = 0; i < n; i++) var -= v[i] * v[i];
        return (mean, Math.Max(var, 0));
    }

    public static double Kernel(double a, double b)
    {
        double r = Math.Abs(a - b), l = Constants.MaternLengthScale; // 0.25
        double s = Math.Sqrt(5.0) * r / l;
        return (1.0 + s + s * s / 3.0) * Math.Exp(-s); // sigma_f^2 = 1
    }

    /// <summary>In-place-style dense Cholesky A = L*L^T. Throws on non-SPD (jitter is caller's job).</summary>
    public static double[,] Cholesky(double[,] a)
    {
        int n = a.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 0) throw new InvalidOperationException($"non-SPD at {i}: {sum}");
                    l[i, i] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        return l;
    }

    public static double[] SolveLt(double[,] l, double[] b) // forward substitution
    {
        int n = b.Length;
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[i, k] * x[k];
            x[i] = s / l[i, i];
        }
        return x;
    }

    public static double[] SolveUt(double[,] u, double[] b) // back substitution
    {
        int n = b.Length;
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = b[i];
            for (int k = i + 1; k < n; k++) s -= u[i, k] * x[k];
            x[i] = s / u[i, i];
        }
        return x;
    }

    private static double[,] Transpose(double[,] m)
    {
        int n = m.GetLength(0);
        var t = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                t[i, j] = m[j, i];
        return t;
    }
}
