using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Core.Config;
using OPTISENS.Core.Input;

namespace OPTISENS.App.ViewModels;

public enum GripStyle { Palm, Claw, Fingertip }

public sealed record GripOption(GripStyle Value, string Label)
{
    public override string ToString() => Label;
}

public readonly record struct HardwareProfile(double Dpi, double PadWidthCm, GripStyle Grip);

public partial class HardwareProfileViewModel : ViewModelBase, IDisposable
{
    private readonly IRawInputService? _input;
    private CancellationTokenSource? _testCts;
    private double _distanceCm;
    private long _sampleCount;
    private double _activeTestDpi = Constants.DpiDefault;

    [ObservableProperty] private decimal _dpi = Constants.DpiDefault;
    [ObservableProperty] private decimal _padWidthCm = (decimal)Constants.PadWidthDefaultCm;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPalmSelected), nameof(IsClawSelected), nameof(IsFingertipSelected))]
    private GripOption _selectedGrip;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _boundsHintText = FormatBounds((double)Constants.PadWidthDefaultCm);

    [ObservableProperty] private bool _isTestingInput;
    [ObservableProperty] private string _inputTestButtonText = "Verify Mouse & DPI";
    [ObservableProperty] private string _inputTestReadout = "Swipe 10 cm on your pad to verify DPI accuracy.";

    partial void OnPadWidthCmChanged(decimal value) => BoundsHintText = FormatBounds((double)value);
    partial void OnDpiChanged(decimal value) =>
        Volatile.Write(ref _activeTestDpi, (double)Math.Clamp(value, Constants.DpiMin, Constants.DpiMax));

    private static string FormatBounds(double padWidthCm)
    {
        double hi = Math.Min(Constants.BoundsMaxCm360, Constants.PadTurnFraction * Math.Max(0, padWidthCm));
        return $"Bounds: {Constants.BoundsMinCm360:F1} cm ↔ {hi:F1} cm";
    }

    public IReadOnlyList<GripOption> GripStyles { get; } =
    [
        new(GripStyle.Palm, "Palm Grip"),
        new(GripStyle.Claw, "Claw Grip"),
        new(GripStyle.Fingertip, "Fingertip Grip"),
    ];

    public bool IsPalmSelected => SelectedGrip.Value == GripStyle.Palm;
    public bool IsClawSelected => SelectedGrip.Value == GripStyle.Claw;
    public bool IsFingertipSelected => SelectedGrip.Value == GripStyle.Fingertip;

    public string AiStatusText { get; } = AppSettingsStore.Load().AiMode switch
    {
        AiProviderMode.OpenAiCompatible => "AI Coach: API Connected",
        AiProviderMode.LocalLfm3 => "AI Coach: Local LFM3 Ready",
        _ => "AI Coach: Off (Default)"
    };

    [RelayCommand]
    private void SelectGrip(string value)
    {
        if (Enum.TryParse<GripStyle>(value, ignoreCase: true, out var grip))
            SelectedGrip = GripStyles.First(g => g.Value == grip);
    }

    [RelayCommand]
    private void SelectDpi(string value)
    {
        if (int.TryParse(value, out int dpi))
            Dpi = Math.Clamp(dpi, Constants.DpiMin, Constants.DpiMax);
    }

    public HardwareProfileViewModel(IRawInputService? input = null)
    {
        _input = input;
        _selectedGrip = GripStyles[0];

        var settings = AppSettingsStore.Load();
        Dpi = (decimal)Math.Clamp(settings.LastDpi, Constants.DpiMin, Constants.DpiMax);
        PadWidthCm = (decimal)Math.Clamp(settings.LastPadWidthCm, Constants.PadWidthMinCm, 120.0);
        if (Enum.TryParse<GripStyle>(settings.LastGrip, ignoreCase: true, out var savedGrip))
            SelectedGrip = GripStyles.First(g => g.Value == savedGrip);
        RecentSessions = new ObservableCollection<CalibrationHistoryEntry>(SessionHistoryStore.LoadAll().Take(5));
    }

    public ObservableCollection<CalibrationHistoryEntry> RecentSessions { get; }
    public IEnumerable<CalibrationHistoryEntry> RecentPreview => RecentSessions.Take(3);
    public bool HasHistory => RecentSessions.Count > 0;
    public CalibrationHistoryEntry? LatestSession => RecentSessions.FirstOrDefault();
    public string LatestSessionSummary => LatestSession is { } latest
        ? $"{latest.RecommendedCm360:F1} cm/360 · {latest.Dpi:F0} DPI"
        : "";

    public event Action<HardwareProfile>? ProfileReady;
    public event Action? AiSettingsRequested;
    public event Action? TestGuideRequested;
    public event Action<CalibrationHistoryEntry>? OpenHistoryEntryRequested;
    public event Action<HardwareProfile>? WarmupRequested;
    public event Action? ArenaSettingsRequested;

    [RelayCommand]
    private void OpenAiSettings() => AiSettingsRequested?.Invoke();

    [RelayCommand]
    private void OpenTestGuide() => TestGuideRequested?.Invoke();

    [RelayCommand]
    private void ToggleInputTest()
    {
        if (_input is null) return;
        if (IsTestingInput)
        {
            StopInputTest();
            return;
        }

        while (_input.Samples.TryRead(out _)) { }
        _distanceCm = 0;
        _sampleCount = 0;
        Volatile.Write(ref _activeTestDpi, (double)Math.Clamp(Dpi, Constants.DpiMin, Constants.DpiMax));
        IsTestingInput = true;
        InputTestButtonText = "Stop DPI Check";
        InputTestReadout = "Move your mouse across the pad…";
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            long windowSamples = 0;
            double hz = 0;
            while (!ct.IsCancellationRequested)
            {
                while (_input.Samples.TryRead(out var s))
                {
                    double dpi = Volatile.Read(ref _activeTestDpi);
                    double counts = Math.Sqrt((double)s.DxCounts * s.DxCounts + (double)s.DyCounts * s.DyCounts);
                    _distanceCm += counts / dpi * 2.54;
                    _sampleCount++;
                    windowSamples++;
                }

                if (sw.ElapsedMilliseconds >= 250)
                {
                    hz = windowSamples / (sw.ElapsedMilliseconds / 1000.0);
                    windowSamples = 0;
                    sw.Restart();
                    double dist = _distanceCm;
                    long total = _sampleCount;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsTestingInput)
                            InputTestReadout = total == 0
                                ? "Waiting for raw mouse motion…"
                                : $"Measured: {dist:F2} cm   ·   {hz:F0} Hz   ({total} samples)";
                    });
                }

                await Task.Delay(16, ct).ConfigureAwait(false);
            }
        }, ct);
    }

    [RelayCommand]
    private void ResetInputTest()
    {
        _distanceCm = 0;
        _sampleCount = 0;
        InputTestReadout = "Reset to 0.00 cm — swipe your mouse to measure.";
    }

    private void StopInputTest()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = null;
        IsTestingInput = false;
        InputTestButtonText = "Verify Mouse & DPI";
    }

    [RelayCommand]
    private void StartCalibration()
    {
        if (!TryValidateProfile()) return;
        StopInputTest();
        SaveLastProfile();
        ProfileReady?.Invoke(new HardwareProfile((double)Dpi, (double)PadWidthCm, SelectedGrip.Value));
    }

    [RelayCommand]
    private void StartWarmup()
    {
        if (!TryValidateProfile()) return;
        StopInputTest();
        SaveLastProfile();
        WarmupRequested?.Invoke(new HardwareProfile((double)Dpi, (double)PadWidthCm, SelectedGrip.Value));
    }

    [RelayCommand]
    private void OpenLatestResults()
    {
        if (LatestSession is { } e) OpenHistoryEntryRequested?.Invoke(e);
    }

    [RelayCommand]
    private void OpenHistoryEntry(CalibrationHistoryEntry entry) => OpenHistoryEntryRequested?.Invoke(entry);

    [RelayCommand]
    private void DeleteHistoryEntry(CalibrationHistoryEntry entry)
    {
        SessionHistoryStore.Delete(entry.Id);
        RecentSessions.Remove(entry);
        OnPropertyChanged(nameof(RecentPreview));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(LatestSession));
        OnPropertyChanged(nameof(LatestSessionSummary));
    }

    [RelayCommand]
    private void OpenArenaSettings() => ArenaSettingsRequested?.Invoke();

    private bool TryValidateProfile()
    {
        ValidationError = null;
        if (Dpi < Constants.DpiMin || Dpi > Constants.DpiMax)
        {
            ValidationError = $"DPI must be between {Constants.DpiMin} and {Constants.DpiMax}.";
            return false;
        }
        if (PadWidthCm < (decimal)Constants.PadWidthMinCm)
        {
            ValidationError = $"Mousepad width must be at least {Constants.PadWidthMinCm} cm.";
            return false;
        }
        return true;
    }

    private void SaveLastProfile()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            AppSettingsStore.Save(settings with
            {
                LastDpi = (double)Dpi,
                LastPadWidthCm = (double)PadWidthCm,
                LastGrip = SelectedGrip.Value.ToString()
            });
        }
        catch { /* best effort persistence */ }
    }

    [RelayCommand]
    private void ExitApp()
    {
        StopInputTest();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose() => StopInputTest();
}
