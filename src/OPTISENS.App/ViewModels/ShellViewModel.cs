using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using OPTISENS.Core.Coaching;
using OPTISENS.Core.Config;
using OPTISENS.Core.Input;
using OPTISENS.Core.Optimization;

namespace OPTISENS.App.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly IRawInputService _input;
    private readonly bool _devMode;
    private string _modelDir;
    private bool _skipAi;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private string? _remediationBanner;

    private HardwareProfileViewModel? _activeProfileVm;
    private HardwareProfile _activeProfile;
    private SessionViewModel? _activeSession;
    private ResultsViewModel? _activeResults;

    public ShellViewModel(IRawInputService input, string modelDir, bool devMode, bool skipAi, string? startupError = null)
    {
        _input = input;
        _modelDir = modelDir;
        _devMode = devMode;
        _skipAi = skipAi;
        _remediationBanner = startupError;

        _input.DeviceCountChanged += count =>
        {
            if (count == 0)
                RemediationBanner = "No mouse device detected. Connect a mouse to run calibration.";
            else if (RemediationBanner?.StartsWith("No mouse", StringComparison.Ordinal) == true)
                RemediationBanner = null;
        };

        if (!_skipAi && !AssetManifest.CheckAssets(_modelDir).IsReady)
        {
            var setupVm = new SetupViewModel(_modelDir);
            setupVm.SetupCompleted += (resolvedDir, skip) =>
            {
                _modelDir = resolvedDir;
                _skipAi = skip;
                CurrentPage = CreateHardwareProfileVm();
            };
            _currentPage = setupVm;
        }
        else
        {
            _currentPage = CreateHardwareProfileVm();
        }
    }

    private HardwareProfileViewModel CreateHardwareProfileVm()
    {
        _activeProfileVm?.Dispose();
        var hw = new HardwareProfileViewModel(_input);
        hw.ProfileReady += OnProfileReady;
        _activeProfileVm = hw;
        return hw;
    }

    private void OnProfileReady(HardwareProfile profile)
    {
        _activeProfileVm?.Dispose();
        _activeProfileVm = null;

        // Always release any previous AI Coach / InferenceSession before starting a new calibration
        _activeResults?.Coach.Dispose();
        _activeResults = null;
        _activeSession?.Dispose();
        _activeSession = null;

        _activeProfile = profile;
        var sessionVm = new SessionViewModel(_input, profile, _devMode);
        sessionVm.SessionFinished += (report, scores, invalid, partial) =>
        {
            var (lo, hi) = ProbeBounds.Compute(profile.PadWidthCm);
            var telemetry = TelemetryBuilder.Build(
                profile.Dpi, profile.PadWidthCm, profile.Grip.ToString().ToLowerInvariant(),
                lo, hi, scores, report);
            string json = TelemetryBuilder.ToJson(telemetry);

            var coachVm = new CoachViewModel();
            var resultsVm = new ResultsViewModel(coachVm);
            resultsVm.BackToMenuRequested += NavigateToProfile;
            resultsVm.RecalibrateRequested += () => OnProfileReady(_activeProfile);
            resultsVm.LoadReport(report, scores, invalid, profile.Dpi, partial);
            _activeResults = resultsVm;
            CurrentPage = resultsVm;

            _ = coachVm.InitializeAsync(_modelDir, _skipAi, json, report.RecommendedCm360, profile.Dpi, lo, hi);
            _activeSession?.Dispose();
            _activeSession = null;
        };

        _activeSession = sessionVm;
        sessionVm.ReturnToMenuRequested += NavigateToProfile;
        sessionVm.ExitRequested += ShutdownApp;
        CurrentPage = sessionVm;
        sessionVm.StartEngine();
    }

    public void NavigateToProfile()
    {
        _activeSession?.Dispose();
        _activeSession = null;
        _activeResults?.Coach.Dispose();
        _activeResults = null;
        CurrentPage = CreateHardwareProfileVm();
    }

    private static void ShutdownApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose()
    {
        _activeProfileVm?.Dispose();
        _activeSession?.Dispose();
        _activeResults?.Coach.Dispose();
    }
}
