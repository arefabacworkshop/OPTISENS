using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Ai;
using OPTISENS.App.Services;
using OPTISENS.Core.Config;

namespace OPTISENS.App.ViewModels;

public sealed partial class AssetFileItem : ObservableObject
{
    public string RelativePath { get; }
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private IBrush _statusBrush;

    public AssetFileItem(string relativePath, string statusText, IBrush statusBrush)
    {
        RelativePath = relativePath;
        _statusText = statusText;
        _statusBrush = statusBrush;
    }
}

public partial class SetupViewModel : ViewModelBase
{
    public string AppVersion { get; } =
        "v" + (typeof(SetupViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0");

    private readonly AssetBootstrapper _bootstrapper = new();
    private CancellationTokenSource? _cts;

    public bool IsReconfigure { get; }
    public string StatusBadgeText => IsReconfigure ? "AI COACH SETTINGS" : "FIRST-RUN SETUP";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryCtaText), nameof(HasLocalModelFiles), nameof(ShowDeleteLocalModel))]
    private string _targetDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalSelected), nameof(IsOpenAiSelected), nameof(IsAiDisabled), nameof(PrimaryCtaText))]
    private bool _isAiEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalSelected), nameof(IsOpenAiSelected), nameof(PrimaryCtaText))]
    private AiProviderMode _selectedProvider = AiProviderMode.LocalLfm3;

    [ObservableProperty] private bool _isChoosingOptions = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeleteLocalModel))]
    private bool _isDownloading;

    [ObservableProperty] private string _statusTitle = "Welcome to OPTISENS";
    [ObservableProperty] private string _statusSubtitle =
        "Choose where OPTISENS stores data, and optionally enable the AI Coach for post-calibration analysis.";

    [ObservableProperty] private string _openAiBaseUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string _openAiModel = "gpt-4o-mini";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private string? _connectionTestStatus;
    [ObservableProperty] private IBrush _connectionTestBrush = Brushes.LightSlateGray;

    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _progressPercentText = "0%";
    [ObservableProperty] private string _currentFileText = "";
    [ObservableProperty] private string _speedEtaText = "";
    [ObservableProperty] private string? _errorMessage;

    public bool IsLocalSelected => IsAiEnabled && SelectedProvider == AiProviderMode.LocalLfm3;
    public bool IsOpenAiSelected => IsAiEnabled && SelectedProvider == AiProviderMode.OpenAiCompatible;
    public bool IsAiDisabled => !IsAiEnabled;
    public bool HasLocalModelFiles =>
        !string.IsNullOrWhiteSpace(TargetDirectory) &&
        AssetManifest.RequiredAssets.Any(s =>
            File.Exists(Path.Combine(TargetDirectory, s.RelativePath)) ||
            File.Exists(Path.Combine(TargetDirectory, s.RelativePath) + ".part"));
    public bool ShowDeleteLocalModel => HasLocalModelFiles && !IsDownloading;

    public string PrimaryCtaText
    {
        get
        {
            if (!IsAiEnabled) return "Save (AI Disabled)";
            if (SelectedProvider == AiProviderMode.OpenAiCompatible) return "Save & Enable API Coach";
            return AssetManifest.CheckAssets(TargetDirectory).IsReady
                ? "Enable Local AI Coach"
                : "Download Model (~1.66 GB) & Enable";
        }
    }

    public ObservableCollection<AssetFileItem> FileItems { get; } = new();
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    public event Action<string, bool>? SetupCompleted;
    public event Action? SetupCancelled;

    public SetupViewModel(string initialDirectory, AppSettings? currentSettings = null, bool isReconfigure = false, AiProviderMode? preselectMode = null)
    {
        IsReconfigure = isReconfigure;
        _targetDirectory = initialDirectory;

        if (preselectMode is AiProviderMode.LocalLfm3 or AiProviderMode.OpenAiCompatible)
        {
            _isAiEnabled = true;
            _selectedProvider = preselectMode.Value;
        }
        else if (currentSettings is not null)
        {
            _isAiEnabled = currentSettings.AiMode is AiProviderMode.LocalLfm3 or AiProviderMode.OpenAiCompatible;
            _selectedProvider = currentSettings.AiMode == AiProviderMode.OpenAiCompatible
                ? AiProviderMode.OpenAiCompatible
                : AiProviderMode.LocalLfm3;
        }
        if (currentSettings is not null)
        {
            if (!string.IsNullOrWhiteSpace(currentSettings.OpenAiBaseUrl))
                _openAiBaseUrl = currentSettings.OpenAiBaseUrl;
            if (!string.IsNullOrWhiteSpace(currentSettings.OpenAiModel))
                _openAiModel = currentSettings.OpenAiModel;
            if (!string.IsNullOrWhiteSpace(currentSettings.OpenAiApiKey))
                _openAiApiKey = currentSettings.OpenAiApiKey;
        }

        if (isReconfigure)
        {
            _statusTitle = "AI Coach & Storage";
            _statusSubtitle = "Update your data directory, toggle AI Coach on or off, or switch between Local LFM3 and an OpenAI-compatible API.";
        }

        RefreshFileChecklist();
    }

    partial void OnTargetDirectoryChanged(string value) => RefreshFileChecklist();

    private void RefreshFileChecklist()
    {
        var check = AssetManifest.CheckAssets(TargetDirectory);
        var missingSet = new HashSet<string>(check.MissingOrInvalid.Select(m => m.RelativePath));

        FileItems.Clear();
        foreach (var spec in AssetManifest.RequiredAssets)
        {
            bool ok = !missingSet.Contains(spec.RelativePath);
            FileItems.Add(new AssetFileItem(
                spec.RelativePath,
                ok ? "Ready" : "Missing",
                ok ? Brushes.MediumSpringGreen : Brushes.LightSlateGray));
        }
    }

    [RelayCommand]
    private void SelectLocalProvider()
    {
        IsAiEnabled = true;
        SelectedProvider = AiProviderMode.LocalLfm3;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void SelectOpenAiProvider()
    {
        IsAiEnabled = true;
        SelectedProvider = AiProviderMode.OpenAiCompatible;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        ConnectionTestStatus = null;
        ErrorMessage = null;
        switch (preset.ToLowerInvariant())
        {
            case "openai":
                OpenAiBaseUrl = "https://api.openai.com/v1";
                OpenAiModel = "gpt-4o-mini";
                break;
            case "openrouter":
                OpenAiBaseUrl = "https://openrouter.ai/api/v1";
                OpenAiModel = "openai/gpt-4o-mini";
                break;
            case "groq":
                OpenAiBaseUrl = "https://api.groq.com/openai/v1";
                OpenAiModel = "llama-3.3-70b-versatile";
                break;
            case "ollama":
                OpenAiBaseUrl = "http://localhost:11434/v1";
                OpenAiModel = "llama3.2";
                OpenAiApiKey = "";
                break;
            case "lmstudio":
                OpenAiBaseUrl = "http://localhost:1234/v1";
                OpenAiModel = "local-model";
                OpenAiApiKey = "";
                break;
            case "custom":
                OpenAiBaseUrl = "https://";
                OpenAiModel = "";
                break;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;
        ErrorMessage = null;
        IsTestingConnection = true;
        ConnectionTestBrush = Brushes.LightSlateGray;
        ConnectionTestStatus = "Testing endpoint…";

        try
        {
            var (ok, message, _) = await OpenAiCoachSession.TestConnectionAsync(
                OpenAiBaseUrl, OpenAiApiKey, OpenAiModel);
            ConnectionTestBrush = ok ? Brushes.MediumSpringGreen : Brushes.IndianRed;
            ConnectionTestStatus = ok ? $"✓ {message}" : $"✕ {message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolderAsync is null) return;
        var chosen = await PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            string leaf = Path.GetFileName(chosen.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            TargetDirectory = string.Equals(leaf, "LFM3-1.7B-ONNX", StringComparison.OrdinalIgnoreCase)
                ? chosen
                : Path.Combine(chosen, "LFM3-1.7B-ONNX");
        }
    }

    [RelayCommand]
    private void ResetDefaultFolder()
    {
        TargetDirectory = AssetManifest.DefaultAssetsDirectory;
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        ErrorMessage = null;

        if (!IsAiEnabled)
        {
            PersistAndFinish(AiProviderMode.Disabled);
            return;
        }

        if (SelectedProvider == AiProviderMode.OpenAiCompatible)
        {
            if (string.IsNullOrWhiteSpace(OpenAiBaseUrl) || !Uri.TryCreate(OpenAiBaseUrl.Trim(), UriKind.Absolute, out _))
            {
                ErrorMessage = "Please enter a valid Base URL (e.g. https://api.openai.com/v1 or http://localhost:11434/v1).";
                return;
            }
            if (string.IsNullOrWhiteSpace(OpenAiModel))
            {
                ErrorMessage = "Please enter a Model name (e.g. gpt-4o-mini or llama3.2).";
                return;
            }
            PersistAndFinish(AiProviderMode.OpenAiCompatible);
            return;
        }

        var check = AssetManifest.CheckAssets(TargetDirectory);
        if (check.IsReady)
        {
            PersistAndFinish(AiProviderMode.LocalLfm3);
            return;
        }

        IsChoosingOptions = false;
        IsDownloading = true;
        StatusTitle = "Downloading AI Coach";
        StatusSubtitle = "Fetching LFM3-1.7B-ONNX into your selected folder. This happens only once.";

        _cts = new CancellationTokenSource();
        var progress = new Progress<AssetDownloadProgress>(p =>
        {
            ProgressFraction = p.OverallFraction;
            ProgressPercentText = $"{p.OverallFraction * 100:F0}%";
            double dlMb = p.FileBytesDownloaded / (1024.0 * 1024.0);
            string totalStr = p.FileTotalBytes is > 0 ? $" / {p.FileTotalBytes.Value / (1024.0 * 1024.0):F1} MB" : "";
            CurrentFileText = $"({p.FileIndex}/{p.TotalFiles}) {p.CurrentFile} — {dlMb:F1}{totalStr}";
            string etaStr = p.Eta is { } eta ? $"ETA {eta:mm\\:ss}" : "Calculating…";
            SpeedEtaText = $"{p.SpeedMBps:F1} MB/s  ·  {etaStr}";

            SetFileStatus(p.CurrentFile, "Downloading…", Brushes.Cyan);
        });

        try
        {
            await _bootstrapper.DownloadMissingAsync(
                TargetDirectory,
                check.MissingOrInvalid,
                progress,
                (rel, ok) => Dispatcher.UIThread.Post(() =>
                    SetFileStatus(rel, ok ? "Verified" : "Failed", ok ? Brushes.MediumSpringGreen : Brushes.IndianRed)),
                _cts.Token);

            PersistAndFinish(AiProviderMode.LocalLfm3);
        }
        catch (OperationCanceledException)
        {
            ResetAfterPause("Welcome to OPTISENS", "Download cancelled. Partial files can be retried anytime, or you can disable AI / switch to an OpenAI-compatible API.");
        }
        catch (Exception ex)
        {
            ResetAfterPause("Download paused", $"Failed to fetch assets: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetFileStatus(string relativePath, string statusText, IBrush brush)
    {
        foreach (var item in FileItems)
        {
            if (item.RelativePath == relativePath)
            {
                item.StatusText = statusText;
                item.StatusBrush = brush;
            }
        }
    }

    private void ResetAfterPause(string title, string error)
    {
        IsDownloading = false;
        IsChoosingOptions = true;
        StatusTitle = title;
        ErrorMessage = error;
        RefreshFileChecklist();
    }

    [RelayCommand]
    private void CancelDownload() => _cts?.Cancel();

    [RelayCommand]
    private void CancelReconfigure() => SetupCancelled?.Invoke();

    [RelayCommand]
    private void DeleteLocalModel()
    {
        if (string.IsNullOrWhiteSpace(TargetDirectory)) return;
        foreach (var spec in AssetManifest.RequiredAssets)
        {
            string full = Path.Combine(TargetDirectory, spec.RelativePath);
            try { if (File.Exists(full)) File.Delete(full); } catch { /* best effort */ }
            try { if (File.Exists(full + ".part")) File.Delete(full + ".part"); } catch { /* best effort */ }
        }
        IsAiEnabled = false;
        var existing = AppSettingsStore.Load();
        AppSettingsStore.Save(existing with { AiMode = AiProviderMode.Disabled });
        RefreshFileChecklist();
        OnPropertyChanged(nameof(HasLocalModelFiles));
        OnPropertyChanged(nameof(ShowDeleteLocalModel));
        OnPropertyChanged(nameof(PrimaryCtaText));
    }

    private void PersistAndFinish(AiProviderMode mode)
    {
        var existing = AppSettingsStore.Load();
        var updated = existing with
        {
            AssetsDirectory = TargetDirectory,
            HasCompletedSetup = true,
            AiMode = mode,
            OpenAiBaseUrl = string.IsNullOrWhiteSpace(OpenAiBaseUrl) ? null : OpenAiBaseUrl.Trim(),
            OpenAiApiKey = string.IsNullOrWhiteSpace(OpenAiApiKey) ? null : OpenAiApiKey.Trim(),
            OpenAiModel = string.IsNullOrWhiteSpace(OpenAiModel) ? null : OpenAiModel.Trim()
        };
        AppSettingsStore.Save(updated);
        IsDownloading = false;
        SetupCompleted?.Invoke(TargetDirectory, mode == AiProviderMode.Disabled);
    }
}
