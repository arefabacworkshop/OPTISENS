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

    [ObservableProperty] private decimal _dpi = Constants.DpiDefault;
    [ObservableProperty] private decimal _padWidthCm = (decimal)Constants.PadWidthDefaultCm;
    [ObservableProperty] private GripOption _selectedGrip;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _boundsHintText = FormatBounds((double)Constants.PadWidthDefaultCm);

    [ObservableProperty] private bool _isTestingInput;
    [ObservableProperty] private string _inputTestButtonText = "Verify Mouse & DPI";
    [ObservableProperty] private string _inputTestReadout = "Swipe 10 cm on your pad to verify DPI accuracy.";

    partial void OnPadWidthCmChanged(decimal value) => BoundsHintText = FormatBounds((double)value);

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

    public HardwareProfileViewModel(IRawInputService? input = null)
    {
        _input = input;
        _selectedGrip = GripStyles[0];
    }

    public event Action<HardwareProfile>? ProfileReady;

    [RelayCommand]
    private void ToggleInputTest()
    {
        if (_input is null) return;
        if (IsTestingInput)
        {
            StopInputTest();
            return;
        }

        _distanceCm = 0;
        _sampleCount = 0;
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
                    double dpi = (double)Math.Clamp(Dpi, Constants.DpiMin, Constants.DpiMax);
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
        ValidationError = null;
        if (Dpi < Constants.DpiMin || Dpi > Constants.DpiMax)
        {
            ValidationError = $"DPI must be between {Constants.DpiMin} and {Constants.DpiMax}.";
            return;
        }
        if (PadWidthCm < (decimal)Constants.PadWidthMinCm)
        {
            ValidationError = $"Mousepad width must be at least {Constants.PadWidthMinCm} cm.";
            return;
        }
        StopInputTest();
        ProfileReady?.Invoke(new HardwareProfile((double)Dpi, (double)PadWidthCm, SelectedGrip.Value));
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
