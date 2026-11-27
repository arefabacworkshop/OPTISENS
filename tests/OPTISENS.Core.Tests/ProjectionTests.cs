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
    public void GameCatalog_AllGames_RoundTripAccurate()
    {
        Assert.Equal(36, GameCatalog.All.Count);
        double[] dpis = [400, 800, 1600];
        double[] cm360s = [15, 30, 50];

        foreach (var game in GameCatalog.All)
            foreach (var dpi in dpis)
                foreach (var cm in cm360s)
                {
                    double sens = game.ToGameSens(dpi, cm);
                    // PUBG is a bounded 0–100 slider; outside that range the inverse is undefined.
                    if (game.Id == "pubg" && (sens <= 0.0 || sens >= 100.0)) continue;
                    double roundTrip = game.ToCm360(dpi, sens);
                    Assert.InRange(Math.Abs(roundTrip - cm), 0, 1e-4);
                }
    }

    [Fact]
    public void GameCatalog_KnownReferenceValues()
    {
        const double dpi = 800, cm = 30;
        Assert.Equal(1.7318, GameCatalog.FindById("cs2")!.ToGameSens(dpi, cm), 4);
        Assert.Equal(0.5443, GameCatalog.FindById("valorant")!.ToGameSens(dpi, cm), 4);
        Assert.Equal(5.7727, GameCatalog.FindById("overwatch2")!.ToGameSens(dpi, cm), 4);
        Assert.Equal(1.7318, GameCatalog.FindById("apex")!.ToGameSens(dpi, cm), 4);
        Assert.Equal(6.8587, GameCatalog.FindById("fortnite")!.ToGameSens(dpi, cm), 4);
        Assert.Equal(17.09, GameCatalog.FindById("r6siege")!.ToGameSens(dpi, cm), 2);
        Assert.Equal(514.35, GameCatalog.FindById("pubg")!.ToCm360(dpi, 50), 2);
        double mc = GameCatalog.FindById("minecraft")!.ToCm360(dpi, 20);
        Assert.InRange(Math.Abs(GameCatalog.FindById("minecraft")!.ToGameSens(dpi, mc) - 20), 0, 1e-6);
    }

    [Fact]
    public void GameCatalog_Filter_SearchAndPinned()
    {
        var pinned = new HashSet<string>(GameCatalog.DefaultPinnedIds);
        Assert.Single(GameCatalog.Filter("apex", GameCategory.All, pinned, pinnedOnly: false));
        Assert.Contains(GameCatalog.Filter(null, GameCategory.TacticalFps, pinned, false), g => g.Id == "cs2");
        Assert.DoesNotContain(GameCatalog.Filter(null, GameCategory.TacticalFps, pinned, false), g => g.Id == "apex");
        var pinnedOnly = GameCatalog.Filter(null, GameCategory.All, pinned, pinnedOnly: true);
        Assert.Equal(GameCatalog.DefaultPinnedIds.Length, pinnedOnly.Count);
    }

    [Fact]
    public void DegPerCount_800_30()
    {
        var p = PhysicalTurnDistance.FromCm(30);
        double dpc = p.DegPerCount(800);
        Assert.InRange(dpc, 0.0381 - 1e-4, 0.0381 + 1e-4);
    }
}
