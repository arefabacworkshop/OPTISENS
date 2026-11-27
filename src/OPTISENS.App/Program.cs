using Avalonia;
using OPTISENS.App.Platform;
using OPTISENS.Core.Input;

namespace OPTISENS.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { OPTISENS.Platform.CursorHelper.ForceUnlockAndShow(); } catch { }
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        string? modelDirArg = null;
        bool devMode = false;
        bool skipAi = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--model-dir" && i + 1 < args.Length) modelDirArg = args[++i];
            else if (args[i] == "--dev") devMode = true;
            else if (args[i] == "--skip-ai") skipAi = true;
        }

        string modelDir = ResolveModelDir(modelDirArg);

        IRawInputService input = PlatformInputFactory.Create();
        string? startupError = null;
        try
        {
            input.Start();
        }
        catch (PlatformInputException ex)
        {
            startupError = $"{ex.Message}\n{ex.Remediation}";
        }

        App.InputService = input;
        App.ModelDir = modelDir;
        App.DevMode = devMode;
        App.SkipAi = skipAi;
        App.StartupError = startupError;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string ResolveModelDir(string? cliArg)
    {
        if (!string.IsNullOrWhiteSpace(cliArg)) return cliArg;

        var saved = OPTISENS.Core.Config.AppSettingsStore.Load().AssetsDirectory;
        if (!string.IsNullOrWhiteSpace(saved)) return saved;

        string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        string xdgModel = Path.Combine(xdg, "optisens", "models", "LFM3-1.7B-ONNX");
        if (Directory.Exists(xdgModel)) return xdgModel;

        return OPTISENS.Core.Config.AssetManifest.DefaultAssetsDirectory;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrashLog(string kind, Exception? ex)
    {
        try
        {
            string baseDir = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));
            string logsDir = Path.Combine(baseDir, "OPTISENS", "logs");
            Directory.CreateDirectory(logsDir);
            string file = Path.Combine(logsDir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
            string content =
                $"[{DateTime.UtcNow:O}] {kind}\n" +
                $"OS: {Environment.OSVersion}\n" +
                $"Runtime: {Environment.Version}\n" +
                $"Exception:\n{ex}\n";
            File.WriteAllText(file, content);
        }
        catch { /* never throw from crash logger */ }
    }
}
