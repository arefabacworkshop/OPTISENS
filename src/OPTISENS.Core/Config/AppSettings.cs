using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPTISENS.Core.Config;

/// <summary>Inference backend preference for the AI Coach engine.</summary>
public enum InferenceBackendMode
{
    DirectMlGpu,
    CpuEco
}

/// <summary>Selected AI Coach provider mode.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiProviderMode>))]
public enum AiProviderMode
{
    Disabled,
    LocalLfm3,
    OpenAiCompatible
}

public sealed record AppSettings(
    string? AssetsDirectory = null,
    string? InferenceBackend = null,
    bool HasCompletedSetup = false,
    AiProviderMode AiMode = AiProviderMode.Disabled,
    string? OpenAiBaseUrl = null,
    string? OpenAiApiKey = null,
    string? OpenAiModel = null,
    bool HasSeenTestGuide = false,
    string[]? PinnedGameIds = null,
    bool SoundMuted = false,
    bool UseInchesPer360 = false,
    double LastDpi = Constants.DpiDefault,
    double LastPadWidthCm = Constants.PadWidthDefaultCm,
    string LastGrip = "Palm",
    double ArenaFovDeg = 103.0,
    string CrosshairStyle = "Cross",
    string CrosshairColorHex = "#00E5FF",
    double CrosshairScale = 1.0,
    double SoundVolume = 0.8)
{
    /// <summary>Resolves the effective backend mode, defaulting by platform when unset.</summary>
    public InferenceBackendMode GetBackendMode() =>
        InferenceBackend?.ToLowerInvariant() switch
        {
            "cpueco" or "cpu" => InferenceBackendMode.CpuEco,
            "directmlgpu" or "directml" or "gpu" => InferenceBackendMode.DirectMlGpu,
            // Default is CpuEco on every platform: the DirectML decode path produces
            // garbage tokens for this quantized model on tested AMD hardware (ORT
            // 1.22-1.24, greedy divergence at the first cache-fed step), while CPU is
            // coherent and faster (19 tok/s measured). Opt in to GPU via the coach toggle.
            _ => InferenceBackendMode.CpuEco
        };
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

public static class AppSettingsStore
{
    public static string GetConfigPath()
    {
        string baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));
        return Path.Combine(baseDir, "OPTISENS", "settings.json");
    }

    public static AppSettings Load(string? pathOverride = null)
    {
        string path = pathOverride ?? GetConfigPath();
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            return loaded with { OpenAiApiKey = SecretProtector.Unprotect(loaded.OpenAiApiKey) };
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? pathOverride = null)
    {
        settings = settings with { OpenAiApiKey = SecretProtector.Protect(settings.OpenAiApiKey) };
        string path = pathOverride ?? GetConfigPath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        File.WriteAllText(path, json);
    }
}
