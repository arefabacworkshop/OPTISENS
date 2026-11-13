namespace OPTISENS.Core.Config;

public sealed record AssetFileSpec(string RelativePath, string Url, long MinBytes);

public sealed record AssetCheckResult(bool IsReady, IReadOnlyList<AssetFileSpec> MissingOrInvalid);

public static class AssetManifest
{
    private const string HfBase = "https://huggingface.co/LiquidAI/LFM3-1.7B-ONNX/resolve/main";

    public static readonly AssetFileSpec[] RequiredAssets =
    [
        new("config.json", $"{HfBase}/config.json", 100),
        new("generation_config.json", $"{HfBase}/generation_config.json", 20),
        new("tokenizer_config.json", $"{HfBase}/tokenizer_config.json", 100),
        new("chat_template.jinja", $"{HfBase}/chat_template.jinja", 100),
        new("tokenizer.json", $"{HfBase}/tokenizer.json", 17_000_000L),
        new(Path.Combine("onnx", "model_q4.onnx"), $"{HfBase}/onnx/model_q4.onnx", 100_000L),
        new(Path.Combine("onnx", "model_q4.onnx_data"), $"{HfBase}/onnx/model_q4.onnx_data", 1_500_000_000L)
    ];

    public static string DefaultAssetsDirectory
    {
        get
        {
            string appLocal = Path.Combine(AppContext.BaseDirectory, "models", "LFM3-1.7B-ONNX");
            if (IsDirectoryWritable(AppContext.BaseDirectory))
                return appLocal;

            if (OperatingSystem.IsMacOS())
            {
                string appSupport = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "OPTISENS", "models", "LFM3-1.7B-ONNX");
                return appSupport;
            }

            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(xdg, "optisens", "models", "LFM3-1.7B-ONNX");
        }
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".optisens-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveInitialDirectory(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride))
            return cliOverride;

        var saved = AppSettingsStore.Load().AssetsDirectory;
        if (!string.IsNullOrWhiteSpace(saved))
            return saved;

        return DefaultAssetsDirectory;
    }

    public static AssetCheckResult CheckAssets(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new AssetCheckResult(false, RequiredAssets);

        var missing = new List<AssetFileSpec>();
        foreach (var spec in RequiredAssets)
        {
            string full = Path.Combine(directory, spec.RelativePath);
            if (!File.Exists(full))
            {
                missing.Add(spec);
                continue;
            }
            var info = new FileInfo(full);
            if (info.Length < spec.MinBytes)
                missing.Add(spec);
        }
        return new AssetCheckResult(missing.Count == 0, missing);
    }
}
