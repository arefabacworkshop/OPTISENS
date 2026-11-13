using OPTISENS.Core.Config;
using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Optimization;
using OPTISENS.Core.Scoring;
using OPTISENS.Core.Units;

namespace OPTISENS.Core.Session;

public enum RoundInvalidReason { None, InsufficientSamples }

public sealed record HudSnapshot(
    RoundPhase Phase,
    int RoundNumber,
    int MaxRounds,
    string BlockStatus,
    double BlockProgress,
    string LiveStatus);

public sealed class RoundController
{
    private readonly IRawInputService _input;
    private readonly OptimizationSession _session;
    private readonly SampleRing _ring = new();
    private readonly KinematicFeatureExtractor _extractor = new();
    private readonly BsiScorer _scorer = new();
    private readonly double _dpi;
    private readonly int _sessionSalt;

    private readonly List<RoundScore> _roundScores = new();
    private readonly List<RoundInvalidReason> _invalidReasons = new();

    private RoundPhase _phase = RoundPhase.Idle;
    private double _sessionStartS;
    private double _phaseStartS;
    private double _roundStartS;
    private int _roundIndex; // 1-based
    private double _currentProbeCm360;
    private ScenarioState? _scenario;

    private double _yawDeg;
    private double _pitchDeg;
    private MouseButtons _prevButtons;
    private double _hitFlashUntilS;

    // SampleRing sequence + time windows for blocks A, B, C
    private long _winAStartSeq, _winAEndSeq;
    private double _winAStartS, _winAEndS;
    private long _winBStartSeq, _winBEndSeq;
    private double _winBStartS, _winBEndS;
    private long _winCStartSeq, _winCEndSeq;
    private double _winCStartS, _winCEndS;

    // Dev overlay diagnostics
    private long _sampleCountInSec;
    private double _rateWindowStartS;
    private double _samplesPerSec;
    private double _swipeCm;
    private double _swipeYawDeltaDeg;
    private double _lastSwipeCm360;

    public RoundController(IRawInputService input, double dpi, double padWidthCm, int sessionSalt = 1337)
    {
        _input = input;
        _dpi = dpi;
        _sessionSalt = sessionSalt;
        _session = new OptimizationSession(padWidthCm, 0);
    }

    public RoundPhase Phase => _phase;
    public int RoundNumber => _roundIndex;
    public IReadOnlyList<RoundScore> RoundScores => _roundScores;
    public IReadOnlyList<RoundInvalidReason> InvalidReasons => _invalidReasons;
    public OptimizationReport? FinalReport { get; private set; }
    public bool Partial { get; private set; }

    public event Action? RequestCursorLock;
    public event Action? RequestCursorUnlock;
    public event Action<HudSnapshot>? HudUpdated;
    public event Action<OptimizationReport, bool>? Completed;

    public void Start(double nowS)
    {
        if (_phase != RoundPhase.Idle) return;
        _sessionStartS = nowS;
        _rateWindowStartS = nowS;
        BeginNextRound(nowS);
    }

    public void Abort(double nowS)
    {
        if (_phase is RoundPhase.Idle or RoundPhase.SessionComplete or RoundPhase.Aborted) return;
        RequestCursorUnlock?.Invoke();
        _scenario?.SealActiveBlock(nowS);
        Partial = true;
        _phase = RoundPhase.Aborted;
        if (_roundScores.Count > 0)
            FinalReport = _session.Report();
        EmitHud(nowS);
        if (FinalReport is not null)
            Completed?.Invoke(FinalReport, true);
    }

    public void Tick(double nowS)
    {
        if (_phase is RoundPhase.Idle or RoundPhase.SessionComplete or RoundPhase.Aborted)
            return;

        bool leftEdge = DrainInput(nowS);
        AdvanceStateMachine(nowS, leftEdge);
        PublishViewportState(nowS);
        EmitHud(nowS);
    }

    private bool DrainInput(double nowS)
    {
        bool leftEdge = false;
        while (_input.Samples.TryRead(out var s))
        {
            _ring.Write(in s);
            _sampleCountInSec++;

            if ((_prevButtons & MouseButtons.Left) == 0 && (s.Buttons & MouseButtons.Left) != 0)
                leftEdge = true;
            _prevButtons = s.Buttons;

            if (_phase is RoundPhase.RoundIntro or RoundPhase.BlockTracking or RoundPhase.BlockFlick or RoundPhase.BlockDot)
                ApplyMotionSample(in s);
        }

        // Also check atomic button snapshot in case button changed without motion sample
        var curButtonsAtomic = _input.CurrentButtons;
        if ((_prevButtons & MouseButtons.Left) == 0 && (curButtonsAtomic & MouseButtons.Left) != 0)
            leftEdge = true;
        _prevButtons = curButtonsAtomic;

        if (nowS - _rateWindowStartS >= 1.0)
        {
            _samplesPerSec = _sampleCountInSec / (nowS - _rateWindowStartS);
            _sampleCountInSec = 0;
            _rateWindowStartS = nowS;
        }

        return leftEdge;
    }

    private void ApplyMotionSample(in RawMotionSample s)
    {
        var (ny, np) = ViewportState.ApplySample(_yawDeg, _pitchDeg, in s, _dpi, _currentProbeCm360);
        double dyaw = ((ny - _yawDeg + 540.0) % 360.0) - 180.0;
        _yawDeg = ny;
        _pitchDeg = np;

        // Dev overlay ruler swipe accumulator
        double stepCm = PhysicalTurnDistance.FromCm(_currentProbeCm360).CountsToCm(s.DxCounts, _dpi);
        if (Math.Sign(stepCm) != 0 && Math.Sign(stepCm) != Math.Sign(_swipeCm) && Math.Abs(_swipeCm) >= 10.0)
        {
            if (Math.Abs(_swipeYawDeltaDeg) > 1e-3)
                _lastSwipeCm360 = Math.Abs(360.0 * _swipeCm / _swipeYawDeltaDeg);
            _swipeCm = 0;
            _swipeYawDeltaDeg = 0;
        }
        _swipeCm += stepCm;
        _swipeYawDeltaDeg += dyaw;
        if (Math.Abs(_swipeCm) >= 10.0 && Math.Abs(_swipeYawDeltaDeg) > 1e-3)
            _lastSwipeCm360 = Math.Abs(360.0 * _swipeCm / _swipeYawDeltaDeg);
    }

    private void AdvanceStateMachine(double nowS, bool leftEdge)
    {
        double elapsed = nowS - _phaseStartS;
        switch (_phase)
        {
            case RoundPhase.RoundIntro:
                if (elapsed >= Constants.RoundIntroS)
                {
                    _phase = RoundPhase.BlockTracking;
                    _phaseStartS = nowS;
                    _winAStartSeq = _ring.Sequence;
                    _winAStartS = nowS;
                    _scenario!.StartBlock(BlockKind.Tracking, nowS, _yawDeg, _pitchDeg);
                }
                break;

            case RoundPhase.BlockTracking:
                _scenario!.Tick(nowS, _yawDeg, _pitchDeg, false);
                if (elapsed >= Constants.BlockTrackingS)
                {
                    _winAEndSeq = _ring.Sequence;
                    _winAEndS = nowS;
                    _scenario.SealActiveBlock(nowS);

                    _phase = RoundPhase.BlockFlick;
                    _phaseStartS = nowS;
                    _winBStartSeq = _ring.Sequence;
                    _winBStartS = nowS;
                    _scenario.StartBlock(BlockKind.Flick, nowS, _yawDeg, _pitchDeg);
                }
                break;

            case RoundPhase.BlockFlick:
                if (_scenario!.Tick(nowS, _yawDeg, _pitchDeg, leftEdge))
                    _hitFlashUntilS = nowS + 0.08;
                if (elapsed >= Constants.BlockFlickS)
                {
                    _winBEndSeq = _ring.Sequence;
                    _winBEndS = nowS;
                    _scenario.SealActiveBlock(nowS);

                    _phase = RoundPhase.BlockDot;
                    _phaseStartS = nowS;
                    _winCStartSeq = _ring.Sequence;
                    _winCStartS = nowS;
                    _scenario.StartBlock(BlockKind.Dot, nowS, _yawDeg, _pitchDeg);
                }
                break;

            case RoundPhase.BlockDot:
                if (_scenario!.Tick(nowS, _yawDeg, _pitchDeg, leftEdge))
                    _hitFlashUntilS = nowS + 0.08;
                if (elapsed >= Constants.BlockDotS)
                {
                    _winCEndSeq = _ring.Sequence;
                    _winCEndS = nowS;
                    _scenario.SealActiveBlock(nowS);
                    EnterRoundScoring(nowS);
                }
                break;

            case RoundPhase.Rest:
                if (elapsed >= Constants.RestS)
                    BeginNextRound(nowS);
                break;
        }
    }

    private void PublishViewportState(double nowS)
    {
        ViewportState.Publish(new ViewportState
        {
            YawDeg = _yawDeg,
            PitchDeg = _pitchDeg,
            SampleSeq = _ring.Sequence,
            ProbeCm360 = _currentProbeCm360,
            Targets = _scenario?.CurrentTargets ?? Array.Empty<TargetSphere>(),
            HitFlash = nowS < _hitFlashUntilS,
            SamplesPerSec = _samplesPerSec,
            DroppedSamples = 0,
            LastSwipeCm360 = _lastSwipeCm360
        });
    }

    private void BeginNextRound(double nowS)
    {
        _roundIndex++;
        _roundStartS = nowS - _sessionStartS;
        _currentProbeCm360 = _session.NextProbeCm360();
        _scenario = new ScenarioState(_roundIndex, _sessionSalt);
        _yawDeg = 0;
        _pitchDeg = 0;
        _phase = RoundPhase.RoundIntro;
        _phaseStartS = nowS;
        RequestCursorLock?.Invoke();
    }

    private void EnterRoundScoring(double nowS)
    {
        _phase = RoundPhase.RoundScoring;
        _phaseStartS = nowS;
        RequestCursorUnlock?.Invoke();

        var (mA, validA) = ScoreWindow(_winAStartSeq, _winAEndSeq, _winAStartS, _winAEndS, Array.Empty<Attempt>());
        mA = mA with { TimeOnTargetFrac = _scenario!.LiveTrackingAccuracy };

        var (mB, validB) = ScoreWindow(_winBStartSeq, _winBEndSeq, _winBStartS, _winBEndS, _scenario.FlickAttempts);
        var (mC, validC) = ScoreWindow(_winCStartSeq, _winCEndSeq, _winCStartS, _winCEndS, _scenario.DotAttempts);

        var reason = (validA && validB && validC) ? RoundInvalidReason.None : RoundInvalidReason.InsufficientSamples;
        _invalidReasons.Add(reason);

        double accB = mB.Attempts > 0 ? (double)mB.Hits / mB.Attempts : 0;
        double accC = mC.Attempts > 0 ? (double)mC.Hits / mC.Attempts : 0;

        var scoredBlocks = new ScoredBlock[]
        {
            new(BlockKind.Tracking, Constants.BlockTrackingS, _scenario.LiveTrackingAccuracy, mA),
            new(BlockKind.Flick,    Constants.BlockFlickS,    accB, mB),
            new(BlockKind.Dot,      Constants.BlockDotS,      accC, mC),
        };

        var roundScore = _scorer.Score(_currentProbeCm360, scoredBlocks, _roundStartS);
        _roundScores.Add(roundScore);
        _session.CompleteRound(roundScore);

        if (_session.CanRunRound && !_session.IsDone)
        {
            _phase = RoundPhase.Rest;
            _phaseStartS = nowS;
        }
        else
        {
            _phase = RoundPhase.SessionComplete;
            FinalReport = _session.Report();
            Completed?.Invoke(FinalReport, false);
        }
    }

    private (BlockMetrics Metrics, bool Valid) ScoreWindow(
        long seqStart, long seqEnd, double tStartS, double tEndS, IReadOnlyList<Attempt> attempts)
    {
        var samples = _ring.SnapshotRange(seqStart, seqEnd);
        if (samples.Length == 0)
            return (new BlockMetrics(0, 0, 0, Array.Empty<double>(), Array.Empty<double>(), 0, attempts.Count(a => a.Hit), attempts.Count), false);

        // Resample using sample timestamp range if available; fall back to tStartS..tEndS
        double s0 = samples[0].TimestampUs / 1e6;
        double s1 = samples[^1].TimestampUs / 1e6;
        double gStart = Math.Min(s0, tStartS);
        double gEnd = Math.Max(s1, tEndS);
        if (gEnd - gStart < Constants.MinBlockSamples * Constants.ResampleDtS)
            gEnd = gStart + Math.Max(0.1, tEndS - tStartS);

        var block = Trajectory.Resample(samples, _dpi, _currentProbeCm360, gStart, gEnd);
        bool valid = block.GridT.Length >= Constants.MinBlockSamples;
        var metrics = _extractor.Extract(block, attempts);
        return (metrics, valid);
    }

    private void EmitHud(double nowS)
    {
        double elapsed = nowS - _phaseStartS;
        (string status, double prog, string live) = _phase switch
        {
            RoundPhase.RoundIntro => (
                $"Get ready ({Math.Max(1, (int)Math.Ceiling(Constants.RoundIntroS - elapsed))})",
                Math.Clamp(elapsed / Constants.RoundIntroS, 0, 1),
                ""),
            RoundPhase.BlockTracking => (
                "Tracking",
                Math.Clamp(elapsed / Constants.BlockTrackingS, 0, 1),
                $"On target {_scenario?.LiveTrackingAccuracy * 100:F0}%"),
            RoundPhase.BlockFlick => (
                "Wide Flicks",
                Math.Clamp(elapsed / Constants.BlockFlickS, 0, 1),
                $"Hits {_scenario?.FlickAttempts.Count(a => a.Hit) ?? 0}/{_scenario?.FlickAttempts.Count ?? 0}"),
            RoundPhase.BlockDot => (
                "Micro-Dots",
                Math.Clamp(elapsed / Constants.BlockDotS, 0, 1),
                $"Hits {_scenario?.DotAttempts.Count(a => a.Hit) ?? 0}/{_scenario?.DotAttempts.Count ?? 0}"),
            RoundPhase.Rest => (
                $"Rest ({Math.Max(1, (int)Math.Ceiling(Constants.RestS - elapsed))}s)",
                Math.Clamp(elapsed / Constants.RestS, 0, 1),
                $"Next: Round {_roundIndex + 1} of {Constants.MaxRounds}"),
            RoundPhase.SessionComplete => ("Session complete", 1.0, ""),
            RoundPhase.Aborted => ("Aborted", 0.0, ""),
            _ => ("Idle", 0.0, "")
        };

        HudUpdated?.Invoke(new HudSnapshot(_phase, _roundIndex, Constants.MaxRounds, status, prog, live));
    }
}
