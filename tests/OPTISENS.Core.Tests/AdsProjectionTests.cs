using OPTISENS.Core.Projection;
using Xunit;

namespace OPTISENS.Core.Tests;

public class AdsProjectionTests
{
    [Fact]
    public void Cs2_Mdv0_IsApproximately08189()
    {
        var entries = AdsProjection.ComputeAll(0.0);
        var cs2 = entries.First(e => e.Game == "CS2");
        double val = double.Parse(cs2.ValueText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(val, 0.8180, 0.8200);
    }

    [Fact]
    public void Ow2_Widow_Mdv0_IsApproximately3789()
    {
        var entries = AdsProjection.ComputeAll(0.0);
        var widow = entries.First(e => e.Game == "Overwatch 2 (Widow/Ana)");
        double val = double.Parse(widow.ValueText.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(val, 37.80, 38.00);
    }
}
