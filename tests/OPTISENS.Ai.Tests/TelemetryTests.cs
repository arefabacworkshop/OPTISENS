using System.Text.Json;
using OPTISENS.Core.Coaching;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class TelemetryTests
{
    [Fact]
    public void Telemetry_RoundTrip()
    {
        var payload = new TelemetryPayload(
            new TelemetrySessionInfo(800, 45, "arm", [10, 60], "2026-11-13T00:00:00Z"),
            [
                new TelemetryRound(
                    10.0, 41.2,
                    new TelemetryComponents(0.42, 0.61, 0.22, 0.35),
                    new TelemetryBlocks(
                        new TelemetryTrackingBlock(0.42, 8210.0),
                        new TelemetryFlickBlock(5, 8, 0.22),
                        new TelemetryDotBlock(6, 10, 187.0)))
            ],
            new TelemetryResult(27.4, [25.1, 29.8], 0.63, 0.03),
            new TelemetryProjections(1.2298, 0.3874, 4.0813, 4.0813));

        string json = TelemetryBuilder.ToJson(payload);
        var roundTrip = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.TelemetryPayload);

        Assert.Equal(payload.Session.Dpi, roundTrip.Session.Dpi);
        Assert.Equal(payload.Session.Grip, roundTrip.Session.Grip);
        Assert.Equal(payload.Rounds.Length, roundTrip.Rounds.Length);
        Assert.Equal(payload.Rounds[0].Bsi, roundTrip.Rounds[0].Bsi);
        Assert.Equal(payload.Result.RecommendedCm360, roundTrip.Result.RecommendedCm360);
        Assert.Equal(payload.Projections, roundTrip.Projections);
    }
}
