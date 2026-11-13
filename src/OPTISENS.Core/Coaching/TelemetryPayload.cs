using System.Text.Json;
using System.Text.Json.Serialization;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Projection;
using OPTISENS.Core.Scoring;

namespace OPTISENS.Core.Coaching;

public readonly record struct TelemetrySessionInfo(
    [property: JsonPropertyName("dpi")] double Dpi,
    [property: JsonPropertyName("pad_width_cm")] double PadWidthCm,
    [property: JsonPropertyName("grip")] string Grip,
    [property: JsonPropertyName("bounds_cm360")] double[] BoundsCm360,
    [property: JsonPropertyName("date_utc")] string DateUtc);

public readonly record struct TelemetryComponents(
    [property: JsonPropertyName("accuracy")] double Accuracy,
    [property: JsonPropertyName("norm_jerk")] double NormJerk,
    [property: JsonPropertyName("e_flick_abs")] double EFlickAbs,
    [property: JsonPropertyName("t_adjust_norm")] double TAdjustNorm);

public readonly record struct TelemetryTrackingBlock(
    [property: JsonPropertyName("time_on_target")] double TimeOnTarget,
    [property: JsonPropertyName("jerk_deg_s3")] double JerkDegS3);

public readonly record struct TelemetryFlickBlock(
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("e_flick_mean")] double EFlickMean);

public readonly record struct TelemetryDotBlock(
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("t_adjust_ms_mean")] double TAdjustMsMean);

public readonly record struct TelemetryBlocks(
    [property: JsonPropertyName("tracking")] TelemetryTrackingBlock Tracking,
    [property: JsonPropertyName("flick")] TelemetryFlickBlock Flick,
    [property: JsonPropertyName("dot")] TelemetryDotBlock Dot);

public readonly record struct TelemetryRound(
    [property: JsonPropertyName("probe_cm360")] double ProbeCm360,
    [property: JsonPropertyName("bsi")] double Bsi,
    [property: JsonPropertyName("components")] TelemetryComponents Components,
    [property: JsonPropertyName("blocks")] TelemetryBlocks Blocks);

public readonly record struct TelemetryResult(
    [property: JsonPropertyName("recommended_cm360")] double RecommendedCm360,
    [property: JsonPropertyName("band")] double[] Band,
    [property: JsonPropertyName("posterior_mean")] double PosteriorMean,
    [property: JsonPropertyName("posterior_std")] double PosteriorStd);

public readonly record struct TelemetryProjections(
    [property: JsonPropertyName("cs2")] double Cs2,
    [property: JsonPropertyName("valorant")] double Valorant,
    [property: JsonPropertyName("ow2")] double Ow2,
    [property: JsonPropertyName("cod")] double Cod);

public readonly record struct TelemetryPayload(
    [property: JsonPropertyName("session")] TelemetrySessionInfo Session,
    [property: JsonPropertyName("rounds")] TelemetryRound[] Rounds,
    [property: JsonPropertyName("result")] TelemetryResult Result,
    [property: JsonPropertyName("projections")] TelemetryProjections Projections);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TelemetryPayload))]
public partial class TelemetryJsonContext : JsonSerializerContext
{
}

public static class TelemetryBuilder
{
    public static TelemetryPayload Build(
        double dpi,
        double padWidthCm,
        string grip,
        double boundsLo,
        double boundsHi,
        IReadOnlyList<RoundScore> scores,
        OptimizationReport report)
    {
        var rounds = new TelemetryRound[scores.Count];
        for (int i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            var bA = s.Blocks.Length > 0 ? s.Blocks[0] : default;
            var bB = s.Blocks.Length > 1 ? s.Blocks[1] : default;
            var bC = s.Blocks.Length > 2 ? s.Blocks[2] : default;

            double eMean = bB.FlickErrors is { Length: > 0 } ? bB.FlickErrors.Select(Math.Abs).Average() : 0;
            double tMean = bC.AdjustLatencyMs is { Length: > 0 } ? bC.AdjustLatencyMs.Average() : 0;

            rounds[i] = new TelemetryRound(
                Math.Round(s.Cm360, 2),
                Math.Round(s.YNorm * 100.0, 1),
                new TelemetryComponents(
                    Math.Round(s.Components.Accuracy, 3),
                    Math.Round(s.Components.NormJerk, 3),
                    Math.Round(s.Components.EFlickAbs, 3),
                    Math.Round(s.Components.TAdjustNorm, 3)),
                new TelemetryBlocks(
                    new TelemetryTrackingBlock(Math.Round(bA.TimeOnTargetFrac, 3), Math.Round(bA.JerkDegPerS3, 1)),
                    new TelemetryFlickBlock(bB.Hits, bB.Attempts, Math.Round(eMean, 3)),
                    new TelemetryDotBlock(bC.Hits, bC.Attempts, Math.Round(tMean, 1))));
        }

        double rec = Math.Round(report.RecommendedCm360, 2);
        return new TelemetryPayload(
            new TelemetrySessionInfo(dpi, padWidthCm, grip, [boundsLo, boundsHi], DateTime.UtcNow.ToString("O")),
            rounds,
            new TelemetryResult(
                rec,
                [Math.Round(report.BandLoCm360, 2), Math.Round(report.BandHiCm360, 2)],
                Math.Round(report.PosteriorMean, 3),
                Math.Round(report.PosteriorStd, 3)),
            new TelemetryProjections(
                Math.Round(GameProjection.ToGameSens(GameEngine.Cs2Source, dpi, rec), 4),
                Math.Round(GameProjection.ToGameSens(GameEngine.ValorantUnreal, dpi, rec), 4),
                Math.Round(GameProjection.ToGameSens(GameEngine.Overwatch2, dpi, rec), 4),
                Math.Round(GameProjection.ToGameSens(GameEngine.CallOfDuty, dpi, rec), 4)));
    }

    public static string ToJson(TelemetryPayload payload) =>
        JsonSerializer.Serialize(payload, TelemetryJsonContext.Default.TelemetryPayload);
}
