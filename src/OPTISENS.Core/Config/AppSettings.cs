using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPTISENS.Core.Config;

/// <summary>Inference backend preference for the AI Coach engine.</summary>
public enum InferenceBackendMode
{
    DirectMlGpu,
    CpuEco
}

public sealed record AppSettings(
    string? AssetsDirectory = null,
    string? InferenceBackend = null)
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
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? pathOverride = null)
    {
        string path = pathOverride ?? GetConfigPath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        File.WriteAllText(path, json);
    }
}
