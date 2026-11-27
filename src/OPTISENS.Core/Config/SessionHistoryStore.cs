using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPTISENS.Core.Config;

public sealed record HistoryRoundRecord(
    double ProbeCm360,
    double YNorm,
    double NoiseVar,
    double Accuracy,
    double NormJerk,
    double EFlickAbs,
    double TAdjustNorm);

public sealed record HistoryCurvePoint(double Cm360, double Mean, double Std);

public sealed record CalibrationHistoryEntry(
    string Id,
    string TimestampUtc,
    double Dpi,
    double PadWidthCm,
    string Grip,
    double RecommendedCm360,
    double BandLoCm360,
    double BandHiCm360,
    double PosteriorMean,
    double PosteriorStd,
    bool Partial,
    string TelemetryJson,
    HistoryRoundRecord[] Rounds,
    HistoryCurvePoint[] Curve);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<CalibrationHistoryEntry>))]
internal partial class SessionHistoryJsonContext : JsonSerializerContext { }

public static class SessionHistoryStore
{
    public const int MaxEntries = 50;

    public static string GetHistoryPath()
    {
        string baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));
        return Path.Combine(baseDir, "OPTISENS", "history.json");
    }

    public static List<CalibrationHistoryEntry> LoadAll(string? pathOverride = null)
    {
        string path = pathOverride ?? GetHistoryPath();
        if (!File.Exists(path)) return [];
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SessionHistoryJsonContext.Default.ListCalibrationHistoryEntry) ?? [];
        }
        catch { return []; }
    }

    public static void Append(CalibrationHistoryEntry entry, string? pathOverride = null)
    {
        var list = LoadAll(pathOverride);
        list.Insert(0, entry);
        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        SaveAll(list, pathOverride);
    }

    public static void Delete(string id, string? pathOverride = null)
    {
        var list = LoadAll(pathOverride);
        if (list.RemoveAll(e => e.Id == id) > 0)
            SaveAll(list, pathOverride);
    }

    public static void Clear(string? pathOverride = null) => SaveAll([], pathOverride);

    private static void SaveAll(List<CalibrationHistoryEntry> list, string? pathOverride)
    {
        string path = pathOverride ?? GetHistoryPath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(list, SessionHistoryJsonContext.Default.ListCalibrationHistoryEntry);
        File.WriteAllText(path, json);
    }
}
