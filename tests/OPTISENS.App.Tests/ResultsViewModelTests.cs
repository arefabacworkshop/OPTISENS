using OPTISENS.App.ViewModels;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Projection;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Session;
using Xunit;

namespace OPTISENS.App.Tests;

public class ResultsViewModelTests
{
    private static (OptimizationReport Report, RoundScore[] Scores, RoundInvalidReason[] Invalid) CreateSampleReport(double targetCm360 = 30.0)
    {
        var curve = Enumerable.Range(0, 10)
            .Select(i => (10.0 + i * 5.0, i == 4 ? 0.85 : 0.70, 0.03))
            .ToArray();
        var obs = new[] { (targetCm360, 0.85, 0.05) };
        var report = new OptimizationReport(targetCm360, targetCm360 - 2.0, targetCm360 + 2.0, 0.85, 0.03, obs, curve);
        var score = new RoundScore(targetCm360, 0.85, new BsiComponents(0.90, 0.25, 0.05, 0.30), Array.Empty<BlockMetrics>(), 0);
        return (report, [score], [RoundInvalidReason.None]);
    }

    [Fact]
    public void LoadReport_WhenRecommendedEquals30_PopulatesHeroText()
    {
        using var coach = new CoachViewModel();
        var vm = new ResultsViewModel(coach);
        var (report, scores, invalid) = CreateSampleReport(30.0);

        vm.LoadReport(report, scores, invalid, dpi: 800, partial: false);
        vm.UseCm360Command.Execute(null);

        Assert.Equal("30.0", vm.HeroValueText);
        Assert.Equal("30.0 cm/360", vm.HeroText);
        Assert.False(vm.IsAdjusted);
    }

    [Fact]
    public void UseCm360_And_UseInches360_AreExplicitNotToggle()
    {
        using var coach = new CoachViewModel();
        var vm = new ResultsViewModel(coach);
        var (report, scores, invalid) = CreateSampleReport(25.4);
        vm.LoadReport(report, scores, invalid, dpi: 800, partial: false);

        vm.UseCm360Command.Execute(null);
        Assert.False(vm.UseInchesPer360);
        vm.UseCm360Command.Execute(null);
        Assert.False(vm.UseInchesPer360);

        vm.UseInches360Command.Execute(null);
        Assert.True(vm.UseInchesPer360);
        Assert.Equal("10.00", vm.HeroValueText);
        vm.UseInches360Command.Execute(null);
        Assert.True(vm.UseInchesPer360);
    }

    [Fact]
    public void Search_Category_And_PinnedFilter_UpdateProjections()
    {
        using var coach = new CoachViewModel();
        var vm = new ResultsViewModel(coach);
        var (report, scores, invalid) = CreateSampleReport(30.0);
        vm.LoadReport(report, scores, invalid, dpi: 800, partial: false);

        Assert.Equal(GameCatalog.All.Count, vm.Projections.Count);

        vm.GameSearchQuery = "valorant";
        Assert.Equal(2, vm.Projections.Count);
        Assert.Contains(vm.Projections, p => p.GameId == "valorant");
        Assert.Contains(vm.Projections, p => p.GameId == "aimlab");

        vm.GameSearchQuery = "";
        vm.SetCategoryCommand.Execute(GameCategory.TacticalFps);
        Assert.All(vm.Projections, p => Assert.Equal("Tactical", p.CategoryLabel));
    }

    [Fact]
    public void AdsProjection_UpdatesWhenMdvPercentChanges()
    {
        using var coach = new CoachViewModel();
        var vm = new ResultsViewModel(coach);
        var (report, scores, invalid) = CreateSampleReport(30.0);
        vm.LoadReport(report, scores, invalid, dpi: 800, partial: false);

        vm.SelectMdvCommand.Execute("0");
        var cs2Focal = vm.AdsEntries.First(e => e.Game == "CS2").ValueText;
        Assert.Equal("0.8189", cs2Focal);

        vm.SelectMdvCommand.Execute("133.333");
        var cs2Default = vm.AdsEntries.First(e => e.Game == "CS2").ValueText;
        Assert.Equal("1.0000", cs2Default);
    }
}
