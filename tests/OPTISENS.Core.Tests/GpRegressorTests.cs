using OPTISENS.Core.Optimization;
using Xunit;

namespace OPTISENS.Core.Tests;

public class GpRegressorTests
{
    [Fact]
    public void Kernel_Diagonal()
    {
        Assert.Equal(1.0, GpRegressor.Kernel(0.3, 0.3));
    }

    [Fact]
    public void Kernel_Symmetric()
    {
        double[] pts = [0.0, 0.15, 0.4, 0.75, 1.0];
        foreach (var a in pts)
            foreach (var b in pts)
                Assert.InRange(Math.Abs(GpRegressor.Kernel(a, b) - GpRegressor.Kernel(b, a)), 0, 1e-15);
    }

    [Fact]
    public void Cholesky_SPD3()
    {
        double[,] a =
        {
            { 4, 12, -16 },
            { 12, 37, -43 },
            { -16, -43, 98 }
        };
        var l = GpRegressor.Cholesky(a);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++) sum += l[i, k] * l[j, k];
                Assert.InRange(Math.Abs(sum - a[i, j]), 0, 1e-12);
            }
    }

    [Fact]
    public void Cholesky_NonSPD_Throws()
    {
        double[,] a = { { 1, 2 }, { 2, 1 } };
        Assert.Throws<InvalidOperationException>(() => GpRegressor.Cholesky(a));
    }

    [Fact]
    public void Gp_InterpolatesNoiseless()
    {
        double[] x = [0.0, 0.25, 0.5, 0.75, 1.0];
        double[] y = [0.2, 0.6, 0.9, 0.5, 0.3];
        double[] noise = [1e-10, 1e-10, 1e-10, 1e-10, 1e-10];
        var gp = new GpRegressor();
        gp.Fit(x, y, noise);

        for (int i = 0; i < x.Length; i++)
        {
            var (mean, _) = gp.Predict(x[i]);
            Assert.InRange(Math.Abs(mean - y[i]), 0, 1e-6);
        }
    }

    [Fact]
    public void Ei_AtOneSigma()
    {
        double sigma = 0.2, best = 0.5, xi = 0.01;
        double mean = best + xi; // mu - y+ - xi = 0 -> z = 0 -> EI = sigma * phi(0)
        double ei = EiAcquirer.Evaluate(mean, sigma * sigma, best, xi);
        double expected = sigma * EiAcquirer.NormPdf(0);
        Assert.InRange(Math.Abs(ei - expected), 0, 1e-6);
    }

    [Fact]
    public void Ei_ZeroSigma()
    {
        Assert.Equal(0.09, EiAcquirer.Evaluate(0.6, 0.0, 0.5, 0.01), 1e-12);
        Assert.Equal(0.0, EiAcquirer.Evaluate(0.4, 0.0, 0.5, 0.01), 1e-12);
    }
}
