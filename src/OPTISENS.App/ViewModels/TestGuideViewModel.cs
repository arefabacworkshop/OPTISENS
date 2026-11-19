using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.App.Controls;
using OPTISENS.Core.Config;

namespace OPTISENS.App.ViewModels;

public partial class TestGuideViewModel : ViewModelBase
{
    public bool IsStandalonePreview { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CurrentMode),
        nameof(IsStep0),
        nameof(IsStep1),
        nameof(IsStep2),
        nameof(CanStartCalibration),
        nameof(ShowNextButton),
        nameof(NextButtonText),
        nameof(StepTitle),
        nameof(ActionRuleBadge),
        nameof(ActionRuleBrush),
        nameof(StepDescription),
        nameof(MeasurementNote))]
    private int _selectedStepIndex;

    [ObservableProperty] private bool _dontShowAutomatically = true;

    public event Action? GuideCompleted;
    public event Action? GuideCancelled;

    public TestGuideViewModel(bool isStandalonePreview = false)
    {
        IsStandalonePreview = isStandalonePreview;
        if (isStandalonePreview)
            _dontShowAutomatically = AppSettingsStore.Load().HasSeenTestGuide;
    }

    public TestGuideMode CurrentMode => SelectedStepIndex switch
    {
        1 => TestGuideMode.Flick,
        2 => TestGuideMode.Dot,
        _ => TestGuideMode.Tracking
    };

    public bool IsStep0 => SelectedStepIndex == 0;
    public bool IsStep1 => SelectedStepIndex == 1;
    public bool IsStep2 => SelectedStepIndex == 2;
    public bool CanStartCalibration => IsStandalonePreview || SelectedStepIndex == 2;
    public bool ShowNextButton => !IsStandalonePreview && SelectedStepIndex < 2;

    public string NextButtonText => SelectedStepIndex switch
    {
        0 => "Next: Flick Shots →",
        1 => "Next: Micro-Dots →",
        _ => "Next →"
    };

    public string StepTitle => SelectedStepIndex switch
    {
        1 => "Phase 2 of 3 — Reflex Flicks",
        2 => "Phase 3 of 3 — Micro-Precision Dots",
        _ => "Phase 1 of 3 — Smooth Tracking"
    };

    public string ActionRuleBadge => SelectedStepIndex switch
    {
        1 => "ACTION: SNAP CROSSHAIR + LEFT-CLICK",
        2 => "ACTION: MICRO-AIM + LEFT-CLICK",
        _ => "ACTION: FOLLOW SMOOTHLY — DO NOT CLICK"
    };

    public IBrush ActionRuleBrush => SelectedStepIndex switch
    {
        0 => new SolidColorBrush(Color.Parse("#00F0FF")),
        _ => new SolidColorBrush(Color.Parse("#2EE59D"))
    };

    public string StepDescription => SelectedStepIndex switch
    {
        1 => "A glowing sphere spawns off-center. Snap your crosshair to it in one clean motion and left-click before it resets.",
        2 => "Small targets appear in a tight cluster. Make controlled micro-adjustments and left-click each dot accurately.",
        _ => "Keep your crosshair centered on the moving sphere as it glides across the arena. Just track it — no clicking is needed."
    };

    public string MeasurementNote => SelectedStepIndex switch
    {
        1 => "Measures: initial snap speed, overshoot vs. undershoot, and correction time.",
        2 => "Measures: fine fingertip/wrist stability and micro-correction precision.",
        _ => "Measures: smoothness, velocity matching, and tracking jitter."
    };

    public string PrimaryCtaText => IsStandalonePreview ? "Got It — Back" : "Start Calibration";

    [RelayCommand]
    private void SelectStep(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
            SelectedStepIndex = Math.Clamp(idx, 0, 2);
    }

    [RelayCommand]
    private void NextStep()
    {
        if (SelectedStepIndex < 2)
            SelectedStepIndex++;
        else if (IsStandalonePreview)
            SelectedStepIndex = 0;
    }

    [RelayCommand]
    private void PrevStep()
    {
        if (SelectedStepIndex > 0)
            SelectedStepIndex--;
        else if (IsStandalonePreview)
            SelectedStepIndex = 2;
    }

    [RelayCommand]
    private void ConfirmGuide()
    {
        if (!IsStandalonePreview || DontShowAutomatically)
        {
            try
            {
                var settings = AppSettingsStore.Load();
                if (settings.HasSeenTestGuide != DontShowAutomatically)
                {
                    AppSettingsStore.Save(settings with { HasSeenTestGuide = DontShowAutomatically });
                }
            }
            catch { /* best effort */ }
        }
        GuideCompleted?.Invoke();
    }

    [RelayCommand]
    private void Back() => GuideCancelled?.Invoke();
}
