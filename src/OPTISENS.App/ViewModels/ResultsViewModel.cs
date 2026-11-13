using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.App.Controls;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Projection;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Session;

namespace OPTISENS.App.ViewModels;

public readonly record struct RoundRow(
    string RoundText,
    string ProbeText,
    string BsiText,
    string AccuracyText,
    string JerkText,
    string FlickText);

public partial class ProjectionCard : ObservableObject
{
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _sensText = "";
    private readonly Func<IClipboard?> _clipboard;

    public ProjectionCard(string gameName, double sens, Func<IClipboard?> clipboard)
    {
        _gameName = gameName;
        _sensText = sens.ToString("F4");
        _clipboard = clipboard;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (_clipboard() is { } clip) await clip.SetTextAsync(SensText);
    }
}

public partial class ResultsViewModel : ViewModelBase
{
    [ObservableProperty] private string _heroText = "";
    [ObservableProperty] private string _heroValueText = "";
    [ObservableProperty] private string _bandText = "";
    [ObservableProperty] private string _dataQualityNote = "";
    [ObservableProperty] private IReadOnlyList<GPPoint>? _curve;
    [ObservableProperty] private IReadOnlyList<ObservationPoint>? _observations;

    public event Action? BackToMenuRequested;
    public event Action? RecalibrateRequested;

    [RelayCommand]
    private void BackToMenu() => BackToMenuRequested?.Invoke();

    [RelayCommand]
    private void Recalibrate() => RecalibrateRequested?.Invoke();

    private double _dpi;

    public ObservableCollection<RoundRow> Rounds { get; } = new();
    public ObservableCollection<ProjectionCard> Projections { get; } = new();
    public CoachViewModel Coach { get; }
    public IClipboard? Clipboard { get; set; }

    public ResultsViewModel(CoachViewModel coach)
    {
        Coach = coach;
        Coach.Cm360Adjusted += UpdateRecommendedCm360;
    }

    public void LoadReport(
        OptimizationReport report,
        IReadOnlyList<RoundScore> scores,
        IReadOnlyList<RoundInvalidReason> invalidReasons,
        double dpi,
        bool partial)
    {
        _dpi = dpi;
        HeroValueText = $"{report.RecommendedCm360:F1}";
        HeroText = $"{report.RecommendedCm360:F1} cm/360";
        BandText = $"plausible band {report.BandLoCm360:F1} – {report.BandHiCm360:F1} cm/360";

        int invalidCount = invalidReasons.Count(r => r != RoundInvalidReason.None);
        DataQualityNote = partial || invalidCount > 0
            ? $"{(partial ? "Session aborted early. " : "")}{(invalidCount > 0 ? $"{invalidCount} round(s) had insufficient samples (<100 ms)." : "")}"
            : "";

        double span = report.Curve[^1].Cm360 - report.Curve[0].Cm360;
        if (span <= 0) span = 1;
        Curve = report.Curve.Select(c => new GPPoint((c.Cm360 - report.Curve[0].Cm360) / span, c.Mean, c.Std)).ToArray();
        Observations = report.Observations.Select(o => new ObservationPoint((o.Cm360 - report.Curve[0].Cm360) / span, o.YNorm)).ToArray();

        Rounds.Clear();
        Rounds.Add(new RoundRow("Round", "Probe", "BSI", "Accuracy", "Jerk", "|E_flick|"));
        for (int i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            Rounds.Add(new RoundRow(
                $"#{i + 1}",
                $"{s.Cm360:F1} cm",
                $"{s.YNorm * 100:F1}",
                $"{s.Components.Accuracy * 100:F0}%",
                $"{s.Components.NormJerk:F2}",
                $"{s.Components.EFlickAbs:F2}"));
        }

        RefreshProjections(report.RecommendedCm360);
    }

    private void UpdateRecommendedCm360(double newCm360)
    {
        HeroValueText = $"{newCm360:F1}";
        HeroText = $"{newCm360:F1} cm/360";
        RefreshProjections(newCm360);
    }

    private void RefreshProjections(double cm360)
    {
        Projections.Clear();
        foreach (var (name, engine) in new (string, GameEngine)[]
                 {
                     ("CS2", GameEngine.Cs2Source),
                     ("Valorant", GameEngine.ValorantUnreal),
                     ("Overwatch 2", GameEngine.Overwatch2),
                     ("Call of Duty", GameEngine.CallOfDuty)
                 })
        {
            Projections.Add(new ProjectionCard(name, GameProjection.ToGameSens(engine, _dpi, cm360), () => Clipboard));
        }
    }
}
