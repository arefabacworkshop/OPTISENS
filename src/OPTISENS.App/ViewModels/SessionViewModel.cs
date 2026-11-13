using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Core.Config;
using OPTISENS.Core.Input;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Session;

namespace OPTISENS.App.ViewModels;

public partial class SessionViewModel : ViewModelBase, IDisposable
{
    private readonly RoundController _controller;
    private readonly bool _devEnabled;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _loopThread;
    private double _lastFrameMs;

    [ObservableProperty] private string _roundText = "Round 1 / 6";
    [ObservableProperty] private string _blockStatus = "Get ready";
    [ObservableProperty] private double _blockProgress;
    [ObservableProperty] private string _liveStatus = "";
    [ObservableProperty] private string? _cursorWarning;
    [ObservableProperty] private bool _showAbortConfirm;

    // Dev overlay (§9.7, --dev flag only)
    [ObservableProperty] private bool _devOverlayVisible;
    [ObservableProperty] private string _devOverlayText = "";

    public event Action? RequestCursorLock;
    public event Action? RequestCursorUnlock;
    public event Action? ReturnToMenuRequested;
    public event Action? ExitRequested;
    public event Action<OptimizationReport, IReadOnlyList<RoundScore>, IReadOnlyList<RoundInvalidReason>, bool>? SessionFinished;

    public SessionViewModel(IRawInputService input, HardwareProfile profile, bool devEnabled)
    {
        _devEnabled = devEnabled;
        _controller = new RoundController(input, profile.Dpi, profile.PadWidthCm);
        _controller.RequestCursorLock += () => Dispatcher.UIThread.Post(() => RequestCursorLock?.Invoke());
        _controller.RequestCursorUnlock += () => Dispatcher.UIThread.Post(() => RequestCursorUnlock?.Invoke());
        _controller.HudUpdated += OnHudUpdated;
        _controller.Completed += (report, partial) =>
        {
            _cts.Cancel();
            Dispatcher.UIThread.Post(() =>
                SessionFinished?.Invoke(report, _controller.RoundScores, _controller.InvalidReasons, partial));
        };
    }

    public void StartEngine()
    {
        if (_loopThread is not null) return;
        _loopThread = new Thread(EngineLoop)
        {
            IsBackground = true,
            Name = "optisens-session",
            Priority = ThreadPriority.AboveNormal
        };
        _loopThread.Start();
    }

    public void ReportFrameTime(double ms) => _lastFrameMs = ms;

    public void ToggleDevOverlay()
    {
#if DEBUG || OPTISENS_DEV
        if (_devEnabled)
            DevOverlayVisible = !DevOverlayVisible;
#else
        if (_devEnabled)
            DevOverlayVisible = !DevOverlayVisible;
#endif
    }

    public void RequestAbortPrompt() => ShowAbortConfirm = true;

    [RelayCommand]
    private void CancelAbort()
    {
        ShowAbortConfirm = false;
        RequestCursorLock?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        ShowAbortConfirm = false;
        _cts.Cancel();
        RequestCursorUnlock?.Invoke();
        _controller.Abort(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        if (_controller.FinalReport is null)
            ReturnToMenuRequested?.Invoke();
    }

    [RelayCommand]
    private void ReturnToMenu()
    {
        ShowAbortConfirm = false;
        _cts.Cancel();
        RequestCursorUnlock?.Invoke();
        ReturnToMenuRequested?.Invoke();
    }

    [RelayCommand]
    private void ExitApp()
    {
        ShowAbortConfirm = false;
        _cts.Cancel();
        RequestCursorUnlock?.Invoke();
        ExitRequested?.Invoke();
    }

    private void EngineLoop()
    {
        double t0 = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _controller.Start(t0);

        while (!_cts.IsCancellationRequested &&
               _controller.Phase is not (RoundPhase.SessionComplete or RoundPhase.Aborted))
        {
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _controller.Tick(now);
            Thread.Sleep(2);
        }
    }

    private void OnHudUpdated(HudSnapshot hud)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RoundText = $"Round {hud.RoundNumber} / {Constants.MaxRounds}";
            BlockStatus = hud.BlockStatus;
            BlockProgress = hud.BlockProgress;
            LiveStatus = hud.LiveStatus;

            if (DevOverlayVisible)
            {
                var snap = ViewportState.Snapshot;
                double fps = _lastFrameMs > 0.01 ? 1000.0 / _lastFrameMs : 0;
                DevOverlayText =
                    $"FPS: {fps:F0} ({_lastFrameMs:F1} ms)\n" +
                    $"Samples/s: {snap.SamplesPerSec:F0}  Dropped: {snap.DroppedSamples}\n" +
                    $"Probe: {snap.ProbeCm360:F2} cm/360\n" +
                    $"Measured (>=10cm swipe): {snap.LastSwipeCm360:F2} cm/360";
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loopThread?.Join(500);
        _cts.Dispose();
    }
}
