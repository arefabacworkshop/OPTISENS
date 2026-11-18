using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.App.Controls;
using OPTISENS.Core.Config;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Projection;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Session;

namespace OPTISENS.App.ViewModels;

public readonly record struct RoundRow(
    int Index,
    double ProbeCm360,
    string RoundText,
    string ProbeText,
    string BsiText,
    string AccuracyText,
    string JerkText,
    string FlickText)
{
    public bool IsDataRow => Index >= 0;
}

public partial class ProjectionCard : ObservableObject
{
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _sensText = "";
    [ObservableProperty] private string _buttonLabel = "Copy";
    private readonly Func<IClipboard?> _clipboard;
    private int _copyVersion;

    public ProjectionCard(string gameName, double sens, Func<IClipboard?> clipboard)
    {
        _gameName = gameName;
        _sensText = sens.ToString("F4");
        _clipboard = clipboard;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (_clipboard() is { } clip)
        {
            await clip.SetTextAsync(SensText);
            int ver = Interlocked.Increment(ref _copyVersion);
            ButtonLabel = "Copied!";
            await Task.Delay(1500);
            if (_copyVersion == ver)
                ButtonLabel = "Copy";
        }
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

    [ObservableProperty] private double _minCm360 = Constants.BoundsMinCm360;
    [ObservableProperty] private double _maxCm360 = Constants.BoundsMaxCm360;
    [ObservableProperty] private double _activeCm360 = 30.0;
    [ObservableProperty] private double _optimalCm360 = 30.0;
    [ObservableProperty] private bool _isAdjusted;
    [ObservableProperty] private decimal _editableDpi = Constants.DpiDefault;
    [ObservableProperty] private int _highlightedRoundIndex = -1;

    public event Action? BackToMenuRequested;
    public event Action? RecalibrateRequested;

    [RelayCommand]
    private void BackToMenu() => BackToMenuRequested?.Invoke();

    [RelayCommand]
    private void Recalibrate() => RecalibrateRequested?.Invoke();

    [RelayCommand]
    private void ResetToOptimal() => ActiveCm360 = OptimalCm360;

    [RelayCommand]
    private void SelectRound(RoundRow row)
    {
        if (!row.IsDataRow) return;
        HighlightedRoundIndex = HighlightedRoundIndex == row.Index ? -1 : row.Index;
        if (HighlightedRoundIndex >= 0)
            ActiveCm360 = Math.Clamp(row.ProbeCm360, MinCm360, MaxCm360);
    }

    public void SelectCm360FromPlot(double cm360)
    {
        HighlightedRoundIndex = -1;
        ActiveCm360 = Math.Clamp(cm360, MinCm360, MaxCm360);
    }

    public ObservableCollection<RoundRow> Rounds { get; } = new();
    public ObservableCollection<ProjectionCard> Projections { get; } = new();
    public CoachViewModel Coach { get; }
    public IClipboard? Clipboard { get; set; }

    public ResultsViewModel(CoachViewModel coach)
    {
        Coach = coach;
        Coach.Cm360Adjusted += cm => ActiveCm360 = Math.Clamp(cm, MinCm360, MaxCm360);
    }

    partial void OnActiveCm360Changed(double value)
    {
        HeroValueText = $"{value:F1}";
        HeroText = $"{value:F1} cm/360";
        IsAdjusted = Math.Abs(value - OptimalCm360) > 0.05;
        RefreshProjections(value);
    }

    partial void OnEditableDpiChanged(decimal value)
    {
        if (value >= Constants.DpiMin && value <= Constants.DpiMax)
            RefreshProjections(ActiveCm360);
    }

    public void LoadReport(
        OptimizationReport report,
        IReadOnlyList<RoundScore> scores,
        IReadOnlyList<RoundInvalidReason> invalidReasons,
        double dpi,
        bool partial)
    {
        MinCm360 = report.Curve[0].Cm360;
        MaxCm360 = report.Curve[^1].Cm360;
        OptimalCm360 = report.RecommendedCm360;
        EditableDpi = (decimal)dpi;
        ActiveCm360 = report.RecommendedCm360;
        BandText = $"plausible band {report.BandLoCm360:F1} – {report.BandHiCm360:F1} cm/360";

        int invalidCount = invalidReasons.Count(r => r != RoundInvalidReason.None);
        DataQualityNote = partial || invalidCount > 0
            ? $"{(partial ? "Session aborted early. " : "")}{(invalidCount > 0 ? $"{invalidCount} round(s) had insufficient samples (<100 ms)." : "")}"
            : "";

        double span = MaxCm360 - MinCm360;
        if (span <= 0) span = 1;
        Curve = report.Curve.Select(c => new GPPoint((c.Cm360 - MinCm360) / span, c.Mean, c.Std)).ToArray();
        Observations = report.Observations.Select(o => new ObservationPoint((o.Cm360 - MinCm360) / span, o.YNorm)).ToArray();

        Rounds.Clear();
        Rounds.Add(new RoundRow(-1, 0, "Round", "Probe", "BSI", "Accuracy", "Jerk", "|E_flick|"));
        for (int i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            Rounds.Add(new RoundRow(
                i,
                s.Cm360,
                $"#{i + 1}",
                $"{s.Cm360:F1} cm",
                $"{s.YNorm * 100:F1}",
                $"{s.Components.Accuracy * 100:F0}%",
                $"{s.Components.NormJerk:F2}",
                $"{s.Components.EFlickAbs:F2}"));
        }

        RefreshProjections(ActiveCm360);
    }

    private void RefreshProjections(double cm360)
    {
        double dpi = (double)Math.Clamp(EditableDpi, Constants.DpiMin, Constants.DpiMax);
        Projections.Clear();
        foreach (var (name, engine) in new (string, GameEngine)[]
                 {
                     ("CS2", GameEngine.Cs2Source),
                     ("Valorant", GameEngine.ValorantUnreal),
                     ("Overwatch 2", GameEngine.Overwatch2),
                     ("Call of Duty", GameEngine.CallOfDuty)
                 })
        {
            Projections.Add(new ProjectionCard(name, GameProjection.ToGameSens(engine, dpi, cm360), () => Clipboard));
        }
    }
}
