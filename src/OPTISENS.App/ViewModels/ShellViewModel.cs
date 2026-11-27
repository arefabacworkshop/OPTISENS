using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using OPTISENS.App.Services;
using OPTISENS.Core.Coaching;
using OPTISENS.Core.Config;
using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Session;

namespace OPTISENS.App.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly IRawInputService _input;
    private readonly bool _devMode;
    private string _modelDir;
    private bool _cliSkipAi;
    private string? _lastTelemetryJson;
    private OptimizationReport? _lastReport;
    private double _lastBoundsLo;
    private double _lastBoundsHi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowBarVisible))]
    private ViewModelBase _currentPage;
    [ObservableProperty] private string? _remediationBanner;

    public bool IsWindowBarVisible => CurrentPage is not SessionViewModel;
    public string AppVersion { get; } =
        "v" + (typeof(ShellViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.5.9");

    private HardwareProfileViewModel? _activeProfileVm;
    private HardwareProfile _activeProfile;
    private SessionViewModel? _activeSession;
    private ResultsViewModel? _activeResults;

    public ShellViewModel(IRawInputService input, string modelDir, bool devMode, bool skipAi, string? startupError = null)
    {
        _input = input;
        _modelDir = modelDir;
        _devMode = devMode;
        _cliSkipAi = skipAi;
        _remediationBanner = startupError;

        _input.DeviceCountChanged += count =>
        {
            if (count == 0)
                RemediationBanner = "No mouse device detected. Connect a mouse to run calibration.";
            else if (RemediationBanner?.StartsWith("No mouse", StringComparison.Ordinal) == true)
                RemediationBanner = null;
        };

        var settings = AppSettingsStore.Load();
        SoundEffectsService.Muted = settings.SoundMuted;
        // Fresh installs start on Hardware Profile with AI off and zero forced downloads.
        _currentPage = CreateHardwareProfileVm();
    }

    private SetupViewModel CreateSetupVm(
        AppSettings settings, bool isReconfigure, bool returnToResults, AiProviderMode? preselectMode = null)
    {
        var setupVm = new SetupViewModel(_modelDir, settings, isReconfigure, preselectMode);
        setupVm.SetupCompleted += (resolvedDir, skip) =>
        {
            _modelDir = resolvedDir;
            if (returnToResults && _activeResults is not null &&
                _lastTelemetryJson is not null && _lastReport is not null)
            {
                CurrentPage = _activeResults;
                _ = _activeResults.Coach.ReinitializeAsync(
                    _modelDir, _cliSkipAi, _lastTelemetryJson,
                    _activeResults.ActiveCm360, (double)_activeResults.EditableDpi,
                    _lastBoundsLo, _lastBoundsHi);
                return;
            }
            CurrentPage = CreateHardwareProfileVm();
        };
        setupVm.SetupCancelled += () =>
        {
            CurrentPage = returnToResults && _activeResults is not null
                ? _activeResults
                : CreateHardwareProfileVm();
        };
        return setupVm;
    }

    private void OpenAiSetup(bool returnToResults, AiProviderMode? preselectMode = null)
    {
        var settings = AppSettingsStore.Load();
        CurrentPage = CreateSetupVm(settings, isReconfigure: true, returnToResults, preselectMode);
    }

    private HardwareProfileViewModel CreateHardwareProfileVm()
    {
        _activeProfileVm?.Dispose();
        var hw = new HardwareProfileViewModel(_input);
        hw.ProfileReady += OnProfileReady;
        hw.AiSettingsRequested += () => OpenAiSetup(returnToResults: false, preselectMode: null);
        hw.TestGuideRequested += () => OpenTestGuide(pendingProfile: null);
        _activeProfileVm = hw;
        return hw;
    }

    private void OpenTestGuide(HardwareProfile? pendingProfile)
    {
        bool isStandalone = pendingProfile is null;
        var guideVm = new TestGuideViewModel(isStandalonePreview: isStandalone);
        guideVm.GuideCompleted += () =>
        {
            if (pendingProfile is { } profile)
                StartCalibrationSession(profile);
            else
                CurrentPage = CreateHardwareProfileVm();
        };
        guideVm.GuideCancelled += () => CurrentPage = CreateHardwareProfileVm();
        CurrentPage = guideVm;
    }

    private void OnProfileReady(HardwareProfile profile)
    {
        _activeProfileVm?.Dispose();
        _activeProfileVm = null;

        if (!AppSettingsStore.Load().HasSeenTestGuide)
        {
            OpenTestGuide(profile);
            return;
        }

        StartCalibrationSession(profile);
    }

    private void StartCalibrationSession(HardwareProfile profile)
    {
        // Always release any previous AI Coach / InferenceSession before starting a new calibration
        _activeResults?.Coach.Dispose();
        _activeResults = null;
        _activeSession?.Dispose();
        _activeSession = null;

        _activeProfile = profile;
        var sessionVm = new SessionViewModel(_input, profile, _devMode);
        sessionVm.SessionFinished += (report, scores, invalid, partial) =>
        {
            ShowResults(profile, report, scores, invalid, partial);
        };

        _activeSession = sessionVm;
        sessionVm.ReturnToMenuRequested += NavigateToProfile;
        sessionVm.ExitRequested += ShutdownApp;
        CurrentPage = sessionVm;
        sessionVm.StartEngine();
    }

    private void ShowResults(
        HardwareProfile profile,
        OptimizationReport report,
        IReadOnlyList<RoundScore> scores,
        IReadOnlyList<RoundInvalidReason> invalid,
        bool partial)
    {
        var (lo, hi) = ProbeBounds.Compute(profile.PadWidthCm);
        var telemetry = TelemetryBuilder.Build(
            profile.Dpi, profile.PadWidthCm, profile.Grip.ToString().ToLowerInvariant(),
            lo, hi, scores, report);
        string json = TelemetryBuilder.ToJson(telemetry);

        _lastTelemetryJson = json;
        _lastReport = report;
        _lastBoundsLo = lo;
        _lastBoundsHi = hi;

        var coachVm = new CoachViewModel();
        coachVm.ConfigureAiRequested += mode => OpenAiSetup(returnToResults: true, preselectMode: mode);
        var resultsVm = new ResultsViewModel(coachVm);
        resultsVm.BackToMenuRequested += NavigateToProfile;
        resultsVm.RecalibrateRequested += () => OnProfileReady(profile);
        resultsVm.LoadReport(report, scores, invalid, profile.Dpi, partial);
        _activeResults = resultsVm;
        CurrentPage = resultsVm;

        _ = coachVm.InitializeAsync(_modelDir, _cliSkipAi, json, report.RecommendedCm360, profile.Dpi, lo, hi);
        _activeSession?.Dispose();
        _activeSession = null;
    }

    /// <summary>Hidden developer shortcut (Ctrl+Shift+T): jump straight to Results &amp; AI Coach with synthetic 6-round data.</summary>
    public void SkipToSampleResults()
    {
        var profile = _activeProfileVm is { } hw
            ? new HardwareProfile((double)hw.Dpi, (double)hw.PadWidthCm, hw.SelectedGrip.Value)
            : _activeProfile.Dpi > 0
                ? _activeProfile
                : new HardwareProfile(Constants.DpiDefault, Constants.PadWidthDefaultCm, GripStyle.Palm);

        _activeProfileVm?.Dispose();
        _activeProfileVm = null;
        _activeResults?.Coach.Dispose();
        _activeResults = null;
        _activeSession?.Dispose();
        _activeSession = null;
        _activeProfile = profile;

        var opt = new OptimizationSession(profile.PadWidthCm, 0);
        var scores = new List<RoundScore>(Constants.MaxRounds);
        var invalid = new List<RoundInvalidReason>(Constants.MaxRounds);
        double sweetSpot = Math.Clamp(34.0, opt.BoundsLoCm360 + 2, opt.BoundsHiCm360 - 2);

        for (int i = 0; i < Constants.MaxRounds; i++)
        {
            double probe = opt.NextProbeCm360();
            double dist = (probe - sweetSpot) / 14.0;
            double yNorm = Math.Clamp(0.84 - 0.35 * dist * dist + (i % 2 == 0 ? 0.015 : -0.015), 0.25, 0.95);
            double acc = Math.Clamp(0.92 - 0.22 * dist * dist, 0.45, 0.96);
            double jerk = Math.Clamp(0.28 + 0.18 * Math.Abs(dist), 0.15, 0.85);
            double eFlick = probe < sweetSpot ? 0.14 * Math.Abs(dist) : -0.10 * Math.Abs(dist);
            double tAdj = Math.Clamp(220 + 95 * Math.Abs(dist), 160, 460);

            var blocks = new[]
            {
                new BlockMetrics(jerk * 1200, jerk * 180, 32, [eFlick], [tAdj], acc, 1, 1),
                new BlockMetrics(jerk * 1100, jerk * 170, 48, [eFlick, eFlick * 0.8], [tAdj], acc, 11, 12),
                new BlockMetrics(jerk * 900, jerk * 140, 22, [eFlick * 0.5], [tAdj * 0.9], acc, 14, 15),
            };
            var score = new RoundScore(
                probe,
                yNorm,
                new BsiComponents(acc, jerk, Math.Abs(eFlick), Math.Clamp(tAdj / 600.0, 0, 1)),
                blocks,
                i * 45.0);
            opt.CompleteRound(score);
            scores.Add(score);
            invalid.Add(RoundInvalidReason.None);
        }

        ShowResults(profile, opt.Report(), scores, invalid, partial: false);
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
