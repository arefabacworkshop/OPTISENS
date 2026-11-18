using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            .InformationalVersion.Split('+')[0] ?? "0.5.5");

    private readonly AssetBootstrapper _bootstrapper = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _targetDirectory;
    [ObservableProperty] private bool _isChoosingLocation = true;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _statusTitle = "Prepare OPTISENS";
    [ObservableProperty] private string _statusSubtitle =
        "OPTISENS needs the LFM3-1.7B AI Coach model (~1.66 GB) to run offline biomechanical coaching. Choose where to store assets, then click Continue.";
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _progressPercentText = "0%";
    [ObservableProperty] private string _currentFileText = "";
    [ObservableProperty] private string _speedEtaText = "";
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<AssetFileItem> FileItems { get; } = new();
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    public event Action<string, bool>? SetupCompleted;

    public SetupViewModel(string initialDirectory)
    {
        _targetDirectory = initialDirectory;
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
    private async Task BrowseFolderAsync()
    {
        if (PickFolderAsync is null) return;
        var chosen = await PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            // If user picks a general parent folder, keep LFM3-1.7B-ONNX subfolder tidy unless they already selected it
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
        var check = AssetManifest.CheckAssets(TargetDirectory);
        if (check.IsReady)
        {
            PersistAndFinish(skipAi: false);
            return;
        }

        IsChoosingLocation = false;
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

            PersistAndFinish(skipAi: false);
        }
        catch (OperationCanceledException)
        {
            ResetAfterPause("Prepare OPTISENS", "Download cancelled. Partial files can be retried anytime.");
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
        IsChoosingLocation = true;
        StatusTitle = title;
        ErrorMessage = error;
        RefreshFileChecklist();
    }

    [RelayCommand]
    private void CancelDownload() => _cts?.Cancel();

    [RelayCommand]
    private void SkipAi() => SetupCompleted?.Invoke(TargetDirectory, true);

    private void PersistAndFinish(bool skipAi)
    {
        AppSettingsStore.Save(new AppSettings(TargetDirectory));
        IsDownloading = false;
        SetupCompleted?.Invoke(TargetDirectory, skipAi);
    }
}
