using System.Collections.ObjectModel;
using System.Globalization;
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
    public string GameId { get; }
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _categoryLabel = "";
    [ObservableProperty] private string _sensText = "";
    [ObservableProperty] private string? _configCommand;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _copyLabel = "Copy";
    [ObservableProperty] private string _copyConfigLabel = "Copy Config";

    private readonly Func<IClipboard?> _clipboard;
    private readonly Action<string> _togglePin;
    private int _copyVersion;

    public ProjectionCard(GameDefinition game, double sens, bool isPinned, Func<IClipboard?> clipboard, Action<string> togglePin)
    {
        GameId = game.Id;
        _gameName = game.Name;
        _categoryLabel = ResultsViewModel.CategoryLabelFor(game.Category);
        _sensText = game.FormatSens(sens);
        _configCommand = game.FormatConfigCommand(sens);
        _note = game.Note;
        _isPinned = isPinned;
        _clipboard = clipboard;
        _togglePin = togglePin;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (_clipboard() is { } clip)
        {
            await clip.SetTextAsync(SensText);
            int ver = Interlocked.Increment(ref _copyVersion);
            CopyLabel = "Copied!";
            await Task.Delay(1500);
            if (_copyVersion == ver)
                CopyLabel = "Copy";
        }
    }

    [RelayCommand]
    private async Task CopyConfigAsync()
    {
        if (ConfigCommand is null || _clipboard() is not { } clip) return;
        await clip.SetTextAsync(ConfigCommand);
        int ver = Interlocked.Increment(ref _copyVersion);
        CopyConfigLabel = "Copied!";
        await Task.Delay(1500);
        if (_copyVersion == ver)
            CopyConfigLabel = "Copy Config";
    }

    [RelayCommand]
    private void TogglePin()
    {
        IsPinned = !IsPinned;
        _togglePin(GameId);
    }
}

public partial class ResultsViewModel : ViewModelBase
{
    [ObservableProperty] private string _heroText = "";
    [ObservableProperty] private string _heroValueText = "";
    [ObservableProperty] private string _heroIn360Text = "";
    [ObservableProperty] private string _heroUnitText = "cm/360";
    [ObservableProperty] private string _bandText = "";
    [ObservableProperty] private string _dataQualityNote = "";
    [ObservableProperty] private IReadOnlyList<GPPoint>? _curve;
    [ObservableProperty] private IReadOnlyList<ObservationPoint>? _observations;

    [ObservableProperty] private double _minCm360 = Constants.BoundsMinCm360;
    [ObservableProperty] private double _maxCm360 = Constants.BoundsMaxCm360;
    [ObservableProperty] private double _activeCm360 = 30.0;
    [ObservableProperty] private double _optimalCm360 = 30.0;
    [ObservableProperty] private bool _isAdjusted;
    [ObservableProperty] private bool _isPreviewingGame;
    [ObservableProperty] private decimal _editableDpi = Constants.DpiDefault;
    [ObservableProperty] private int _highlightedRoundIndex = -1;

    // Game sensitivity converter state
    [ObservableProperty] private string _gameSearchQuery = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllCategoryActive), nameof(IsTacticalActive), nameof(IsHeroActive),
        nameof(IsBattleRoyaleActive), nameof(IsArenaActive), nameof(IsMilSimActive), nameof(IsOtherActive))]
    private GameCategory _selectedCategory = GameCategory.All;
    [ObservableProperty] private bool _showPinnedOnly;
    [ObservableProperty] private bool _useInchesPer360;
    [ObservableProperty] private string _edpiReadout = "";

    // Compare / Convert-from-existing-game bar
    [ObservableProperty] private GameDefinition _sourceGame = GameCatalog.All[0];
    [ObservableProperty] private string _sourceSensInput = "";
    [ObservableProperty] private decimal _sourceDpiInput = Constants.DpiDefault;
    [ObservableProperty] private string _comparisonSummary = "";
    [ObservableProperty] private string _gameCountText = "";

    public bool AllCategoryActive => SelectedCategory == GameCategory.All;
    public bool IsTacticalActive => SelectedCategory == GameCategory.TacticalFps;
    public bool IsHeroActive => SelectedCategory == GameCategory.HeroShooter;
    public bool IsBattleRoyaleActive => SelectedCategory == GameCategory.BattleRoyale;
    public bool IsArenaActive => SelectedCategory == GameCategory.ArenaAndTrainers;
    public bool IsMilSimActive => SelectedCategory == GameCategory.ExtractionAndMilSim;
    public bool IsOtherActive => SelectedCategory == GameCategory.SandboxAndOther;

    public IReadOnlyList<GameDefinition> AllGames => GameCatalog.All;
    public IReadOnlyList<GameCategory> Categories => Enum.GetValues<GameCategory>().ToArray();

    private readonly HashSet<string> _pinnedIds;

    public event Action? BackToMenuRequested;
    public event Action? RecalibrateRequested;

    [RelayCommand]
    private void BackToMenu() => BackToMenuRequested?.Invoke();

    [RelayCommand]
    private void Recalibrate() => RecalibrateRequested?.Invoke();

    [RelayCommand]
    private void SetCategory(GameCategory category) => SelectedCategory = category;

    [RelayCommand]
    private void TogglePinnedOnly() => ShowPinnedOnly = !ShowPinnedOnly;

    [RelayCommand]
    private void ToggleUnit() => UseInchesPer360 = !UseInchesPer360;

    [RelayCommand]
    private void UseCm360() => UseInchesPer360 = false;

    [RelayCommand]
    private void UseInches360() => UseInchesPer360 = true;

    [RelayCommand]
    private void SelectDpi(string value)
    {
        if (int.TryParse(value, out int dpi))
            EditableDpi = Math.Clamp(dpi, Constants.DpiMin, Constants.DpiMax);
    }

    [RelayCommand]
    private void ResetToOptimal()
    {
        IsPreviewingGame = false;
        ActiveCm360 = OptimalCm360;
    }

    [RelayCommand]
    private void SelectRound(RoundRow row)
    {
        if (!row.IsDataRow) return;
        HighlightedRoundIndex = HighlightedRoundIndex == row.Index ? -1 : row.Index;
        if (HighlightedRoundIndex >= 0)
            ActiveCm360 = Math.Clamp(row.ProbeCm360, MinCm360, MaxCm360);
    }

    /// <summary>Previews the user's current game sensitivity as cm/360 across the catalog.</summary>
    [RelayCommand]
    private void ApplySourceGameCm360()
    {
        if (!TrySourceSens(out double sens)) return;
        double dpi = (double)Math.Clamp(SourceDpiInput, Constants.DpiMin, Constants.DpiMax);
        ActiveCm360 = Math.Clamp(SourceGame.ToCm360(dpi, sens), MinCm360, MaxCm360);
        IsPreviewingGame = true;
        UpdateComparisonSummary();
    }

    private bool TrySourceSens(out double sens) =>
        double.TryParse(SourceSensInput, NumberStyles.Float, CultureInfo.InvariantCulture, out sens) && sens > 0;

    private void UpdateComparisonSummary()
    {
        if (!TrySourceSens(out double sens))
        {
            ComparisonSummary = "Enter your current in-game sensitivity to compare with the optimum.";
            return;
        }
        double dpi = (double)Math.Clamp(SourceDpiInput, Constants.DpiMin, Constants.DpiMax);
        double currentCm360 = SourceGame.ToCm360(dpi, sens);
        double optimal = OptimalCm360;
        double deltaPct = optimal > 0 ? (currentCm360 / optimal - 1.0) * 100.0 : 0;
        string direction = deltaPct >= 0 ? "faster" : "slower";
        ComparisonSummary =
            $"Your current {SourceGame.ShortName} {SourceGame.FormatSens(sens)} @ {dpi:F0} DPI = " +
            $"{currentCm360:F1} cm/360 → Optimal is {optimal:F1} cm/360 ({Math.Abs(deltaPct):F0}% {direction})";
    }

    public void SelectCm360FromPlot(double cm360)
    {
        HighlightedRoundIndex = -1;
        IsPreviewingGame = false;
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

        var settings = AppSettingsStore.Load();
        _useInchesPer360 = settings.UseInchesPer360;
        _pinnedIds = new HashSet<string>(settings.PinnedGameIds ?? GameCatalog.DefaultPinnedIds, StringComparer.OrdinalIgnoreCase);
    }

    public static string CategoryLabelFor(GameCategory category) => category switch
    {
        GameCategory.TacticalFps => "Tactical",
        GameCategory.HeroShooter => "Hero",
        GameCategory.BattleRoyale => "BR",
        GameCategory.ArenaAndTrainers => "Arena",
        GameCategory.ExtractionAndMilSim => "Mil-Sim",
        _ => "Other"
    };

    partial void OnActiveCm360Changed(double value)
    {
        UpdateHeroText(value);
        RefreshProjections(value);
    }

    partial void OnEditableDpiChanged(decimal value)
    {
        if (value >= Constants.DpiMin && value <= Constants.DpiMax)
        {
            Coach.UpdateDpi((double)value);
            RefreshProjections(ActiveCm360);
        }
    }

    partial void OnUseInchesPer360Changed(bool value)
    {
        UpdateHeroText(ActiveCm360);
        try
        {
            var settings = AppSettingsStore.Load();
            AppSettingsStore.Save(settings with { UseInchesPer360 = value });
        }
        catch { /* best effort persistence */ }
    }

    partial void OnGameSearchQueryChanged(string value) => RefreshProjections(ActiveCm360);
    partial void OnSelectedCategoryChanged(GameCategory value) => RefreshProjections(ActiveCm360);
    partial void OnShowPinnedOnlyChanged(bool value) => RefreshProjections(ActiveCm360);
    partial void OnSourceGameChanged(GameDefinition value)
    {
        UpdateEdpiReadout();
        UpdateComparisonSummary();
    }
    partial void OnSourceSensInputChanged(string value) => UpdateComparisonSummary();
    partial void OnSourceDpiInputChanged(decimal value) => UpdateComparisonSummary();

    private void UpdateHeroText(double value)
    {
        HeroValueText = UseInchesPer360 ? $"{value / 2.54:F2}" : $"{value:F1}";
        HeroText = UseInchesPer360 ? $"{value / 2.54:F2} in/360" : $"{value:F1} cm/360";
        HeroUnitText = UseInchesPer360 ? "in/360" : "cm/360";
        HeroIn360Text = UseInchesPer360 ? $"{value:F1} cm/360" : $"{value / 2.54:F2} in/360";
        IsAdjusted = Math.Abs(value - OptimalCm360) > 0.05;
        UpdateEdpiReadout();
    }

    private void UpdateEdpiReadout()
    {
        double dpi = (double)Math.Clamp(EditableDpi, Constants.DpiMin, Constants.DpiMax);
        double sens = SourceGame.ToGameSens(dpi, ActiveCm360);
        EdpiReadout = $"eDPI ({SourceGame.ShortName}): {dpi * sens:F0} · {ActiveCm360 / 2.54:F2} in/360";
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

        // Hero text must be set unconditionally: OnActiveCm360Changed does not fire
        // when the recommended value equals the previous value (e.g. exactly 30.0).
        UpdateHeroText(ActiveCm360);
        IsAdjusted = false;

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

    private void TogglePinned(string gameId)
    {
        if (!_pinnedIds.Remove(gameId))
            _pinnedIds.Add(gameId);
        try
        {
            var settings = AppSettingsStore.Load();
            AppSettingsStore.Save(settings with { PinnedGameIds = _pinnedIds.ToArray() });
        }
        catch { /* best effort persistence */ }
        if (ShowPinnedOnly)
            RefreshProjections(ActiveCm360);
    }

    private void RefreshProjections(double cm360)
    {
        double dpi = (double)Math.Clamp(EditableDpi, Constants.DpiMin, Constants.DpiMax);
        var games = GameCatalog.Filter(GameSearchQuery, SelectedCategory, _pinnedIds, ShowPinnedOnly);
        Projections.Clear();
        foreach (var game in games)
        {
            double sens = game.ToGameSens(dpi, cm360);
            Projections.Add(new ProjectionCard(game, sens, _pinnedIds.Contains(game.Id), () => Clipboard, TogglePinned));
        }
        GameCountText = $"{Projections.Count} games";
        UpdateEdpiReadout();
    }
}
