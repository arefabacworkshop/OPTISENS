using OPTISENS.Core.Config;
using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Session;
using OPTISENS.Core.Tests.Fixtures;
using Xunit;

namespace OPTISENS.Core.Tests;

public class RoundControllerTests
{
    [Fact]
    public void RoundController_Timeline()
    {
        using var input = new FakeRawInputService();
        input.Start();
        var ctrl = new RoundController(input, dpi: 800, padWidthCm: 45.0);

        var transitions = new List<(double TimeS, RoundPhase Phase)>();
        RoundPhase prev = ctrl.Phase;

        ctrl.Start(0.0);
        transitions.Add((0.0, ctrl.Phase));
        prev = ctrl.Phase;

        // Simulate 100 Hz ticks over 1 full round (43s) + rest
        for (int ms = 10; ms <= 45_000; ms += 10)
        {
            double t = ms / 1000.0;
            input.Emit(new RawMotionSample(ms * 1000L, 1, 0, MouseButtons.None));
            ctrl.Tick(t);
            if (ctrl.Phase != prev)
            {
                transitions.Add((t, ctrl.Phase));
                prev = ctrl.Phase;
            }
        }

        Assert.Contains(transitions, x => x.Phase == RoundPhase.RoundIntro && Math.Abs(x.TimeS - 0.0) < 0.02);
        Assert.Contains(transitions, x => x.Phase == RoundPhase.BlockTracking && Math.Abs(x.TimeS - 3.0) < 0.02);
        Assert.Contains(transitions, x => x.Phase == RoundPhase.BlockFlick && Math.Abs(x.TimeS - 18.0) < 0.02);
        Assert.Contains(transitions, x => x.Phase == RoundPhase.BlockDot && Math.Abs(x.TimeS - 33.0) < 0.02);
        Assert.Contains(transitions, x => x.Phase == RoundPhase.Rest && Math.Abs(x.TimeS - 43.0) < 0.02);
    }

    [Fact]
    public void RoundController_PauseAndResume_FreezesTimerAndPreservesTimeline()
    {
        using var input = new FakeRawInputService();
        input.Start();
        var ctrl = new RoundController(input, dpi: 800, padWidthCm: 45.0);
        ctrl.Start(0.0);

        for (int ms = 10; ms <= 1000; ms += 10) ctrl.Tick(ms / 1000.0);
        Assert.Equal(RoundPhase.RoundIntro, ctrl.Phase);

        ctrl.RequestPause();
        ctrl.Tick(1.0);
        Assert.True(ctrl.IsPaused);
        for (int ms = 1100; ms <= 2000; ms += 10) ctrl.Tick(ms / 1000.0);
        Assert.Equal(RoundPhase.RoundIntro, ctrl.Phase);

        ctrl.RequestResume();
        ctrl.Tick(3.0);
        Assert.False(ctrl.IsPaused);
        Assert.Equal(RoundPhase.RoundIntro, ctrl.Phase);

        ctrl.Tick(5.05);
        Assert.Equal(RoundPhase.BlockTracking, ctrl.Phase);
    }

    [Fact]
    public void ScenarioState_SealActiveBlock_DoesNotRecordUnearnedMiss()
    {
        var scenario = new ScenarioState(1);
        scenario.StartBlock(BlockKind.Flick, 0.0, 0.0, 0.0);
        Assert.Empty(scenario.FlickAttempts);
        Assert.Single(scenario.CurrentTargets);

        scenario.SealActiveBlock(0.05);
        Assert.Empty(scenario.FlickAttempts);
        Assert.Empty(scenario.CurrentTargets);
    }

    [Fact]
    public void RoundVariant_AllSixVariants_PreserveIsoDifficultyBounds()
    {
        for (int round = 1; round <= Constants.MaxRounds; round++)
        {
            var variant = RoundVariant.ForRound(round);
            Assert.Equal(round, variant.RoundNumber);

            for (double t = 0; t <= Constants.BlockTrackingS; t += 0.05)
            {
                var (yaw, pitch) = ScenarioState.ComputeTrackingOffset(variant.Tracking, t);
                Assert.InRange(Math.Abs(yaw), 0, 20.0);
                Assert.InRange(Math.Abs(pitch), 0, 13.5);
            }

            var flick = new ScenarioState(round);
            flick.StartBlock(BlockKind.Flick, 0.0, 0.0, 0.0);
            Assert.Equal(Constants.FlickTargetRadiusDeg, flick.CurrentTargets[0].RadiusDeg);

            var dot = new ScenarioState(round);
            dot.StartBlock(BlockKind.Dot, 0.0, 0.0, 0.0);
            Assert.Equal(Constants.DotRadiusDeg, dot.CurrentTargets[0].RadiusDeg);
        }
    }

    [Fact]
    public void Integration_EndToEnd_SixVirtualRounds()
    {
        using var input = new FakeRawInputService();
        input.Start();
        var ctrl = new RoundController(input, dpi: 800, padWidthCm: 45.0);

        ctrl.Start(0.0);
        double t = 0.0;
        const double dt = 0.01; // 100 Hz tick
        int guard = 0;

        while (ctrl.Phase != RoundPhase.SessionComplete && guard++ < 40_000)
        {
            t += dt;
            var (samples, _) = SyntheticRound.Generate(30.0, 800, guard, durationS: 0.01);
            foreach (var s in samples)
                input.Emit(s with { TimestampUs = (long)(t * 1_000_000) });
            ctrl.Tick(t);
        }

        Assert.Equal(RoundPhase.SessionComplete, ctrl.Phase);
        Assert.NotNull(ctrl.FinalReport);
        var report = ctrl.FinalReport!;
        Assert.Equal(Constants.MaxRounds, report.Observations.Length);

        // D18: round weights decrease over time -> observation noise increases monotonically
        for (int i = 1; i < report.Observations.Length; i++)
            Assert.True(report.Observations[i].NoiseVar > report.Observations[i - 1].NoiseVar,
                $"Expected noise[{i}] > noise[{i - 1}]");
    }
}
