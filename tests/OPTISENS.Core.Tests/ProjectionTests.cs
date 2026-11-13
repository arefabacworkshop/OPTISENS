using OPTISENS.Core.Projection;
using OPTISENS.Core.Units;
using Xunit;

namespace OPTISENS.Core.Tests;

public class ProjectionTests
{
    [Fact]
    public void Projection_Cs2_WorkedExample()
    {
        double sens = GameProjection.ToGameSens(GameEngine.Cs2Source, 800, 30);
        Assert.InRange(sens, 1.7318 - 1e-4, 1.7318 + 1e-4);
    }

    [Fact]
    public void Projection_Valorant_WorkedExample()
    {
        double sens = GameProjection.ToGameSens(GameEngine.ValorantUnreal, 800, 30);
        Assert.InRange(sens, 0.5443 - 1e-4, 0.5443 + 1e-4);
    }

    [Fact]
    public void Projection_Ow2_WorkedExample()
    {
        double sens = GameProjection.ToGameSens(GameEngine.Overwatch2, 800, 30);
        Assert.InRange(sens, 5.7727 - 1e-4, 5.7727 + 1e-4);
    }

    [Fact]
    public void Projection_RoundTrip()
    {
        var engines = Enum.GetValues<GameEngine>();
        double[] dpis = [400, 800, 1600];
        double[] cm360s = [12, 30, 55];

        foreach (var e in engines)
            foreach (var dpi in dpis)
                foreach (var cm in cm360s)
                {
                    double sens = GameProjection.ToGameSens(e, dpi, cm);
                    double roundTripCm = GameProjection.ToCm360(e, dpi, sens);
                    double roundTripSens = GameProjection.ToGameSens(e, dpi, roundTripCm);
                    Assert.InRange(Math.Abs(roundTripSens - sens), 0, 1e-9);
                }
    }

    [Fact]
    public void DegPerCount_800_30()
    {
        var p = PhysicalTurnDistance.FromCm(30);
        double dpc = p.DegPerCount(800);
        Assert.InRange(dpc, 0.0381 - 1e-4, 0.0381 + 1e-4);
    }
}
