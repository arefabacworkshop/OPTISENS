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
    string LiveStatus,
    int Streak = 0,
    double LastRoundBsi = 0,
    double LastRoundAccuracy = 0,
    string RoundTitle = "",
    string RoundSubtitle = "",
    string NextRoundPreview = "",
    double BestBsiSoFar = 0,
    bool IsNewBestBsi = false);

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
    private double _missFlashUntilS;
    private volatile bool _skipRestRequested;
    private volatile bool _pauseRequested;
    private volatile bool _resumeRequested;
    private volatile bool _abortRequested;
    private bool _isPaused;
    private double _pausedAtS;
    private int _invalidRetriesInRound;
    private bool _retryPending;
    private const int MaxInvalidRetriesPerRound = 2;
    private double _lastHitAtS = -99.0;
    private double _lastHitAzDeg;
    private double _lastHitElDeg;
    private double _bestBsi;
    private bool _isNewBestBsi;
    private long _droppedBaseline;

    // Warmup mode: unscored 30s drill at the midpoint probe (3x 10s blocks, 2s intro)
    private readonly bool _isWarmup;
    private readonly double _roundIntroS;
    private readonly double _blockTrackingS;
    private readonly double _blockFlickS;
    private readonly double _blockDotS;

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
    private readonly double _horizontalFovDeg;

    public RoundController(IRawInputService input, double dpi, double padWidthCm, int sessionSalt = 1337, bool isWarmup = false)
    {
        _input = input;
        _dpi = dpi;
        _sessionSalt = sessionSalt;
        _session = new OptimizationSession(padWidthCm, 0);
        _isWarmup = isWarmup;
        _roundIntroS = isWarmup ? 2.0 : Constants.RoundIntroS;
        _blockTrackingS = isWarmup ? 10.0 : Constants.BlockTrackingS;
        _blockFlickS = isWarmup ? 10.0 : Constants.BlockFlickS;
        _blockDotS = isWarmup ? 10.0 : Constants.BlockDotS;
        _horizontalFovDeg = Math.Clamp(AppSettingsStore.Load().ArenaFovDeg, 70.0, 130.0);
    }

    public RoundPhase Phase => _phase;
    public bool IsPaused => _isPaused;
    public int RoundNumber => _roundIndex;
    public IReadOnlyList<RoundScore> RoundScores => _roundScores;
    public IReadOnlyList<RoundInvalidReason> InvalidReasons => _invalidReasons;
    public OptimizationReport? FinalReport { get; private set; }
    public bool Partial { get; private set; }

    public event Action? RequestCursorLock;
    public event Action? RequestCursorUnlock;
    public event Action<HudSnapshot>? HudUpdated;
    public event Action<OptimizationReport, bool>? Completed;
    public event Action? WarmupCompleted;
    public event Action<int>? TargetHit;
    public event Action? TargetMissed;
    public event Action<RoundPhase>? PhaseChanged;

    public void Start(double nowS)
    {
        if (_phase != RoundPhase.Idle) return;
        _sessionStartS = nowS;
        _rateWindowStartS = nowS;
        _droppedBaseline = _input.DroppedSamples;
        BeginNextRound(nowS);
    }

    public void RequestSkipRest() => _skipRestRequested = true;
    public void RequestPause() => _pauseRequested = true;
    public void RequestResume() => _resumeRequested = true;
    public void RequestAbort() => _abortRequested = true;

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

        if (_abortRequested)
        {
            _abortRequested = false;
            Abort(nowS);
            return;
        }

        if (_pauseRequested)
        {
            _pauseRequested = false;
            if (!_isPaused)
            {
                _isPaused = true;
                _pausedAtS = nowS;
                RequestCursorUnlock?.Invoke();
            }
        }

        if (_resumeRequested)
        {
            _resumeRequested = false;
            if (_isPaused)
            {
                double delta = Math.Max(0, nowS - _pausedAtS);
                _phaseStartS += delta;
                _sessionStartS += delta;
                _roundStartS += delta;
                _rateWindowStartS += delta;
                _hitFlashUntilS += delta;
                _missFlashUntilS += delta;
                _winAStartS += delta;
                _winAEndS += delta;
                _winBStartS += delta;
                _winBEndS += delta;
                _winCStartS += delta;
                _winCEndS += delta;
                _scenario?.ShiftTime(delta);
                while (_input.Samples.TryRead(out _)) { }
                _prevButtons = _input.CurrentButtons;
                _isPaused = false;
                RequestCursorLock?.Invoke();
            }
        }

        if (_isPaused)
        {
            while (_input.Samples.TryRead(out _)) { }
            return;
        }

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
        double dyaw = ScenarioState.SignedYawDeltaDeg(_yawDeg, ny);
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
                if (elapsed >= _roundIntroS)
                {
                    _phase = RoundPhase.BlockTracking;
                    _phaseStartS = nowS;
                    _winAStartSeq = _ring.Sequence;
                    _winAStartS = nowS;
                    _scenario!.StartBlock(BlockKind.Tracking, nowS, _yawDeg, _pitchDeg);
                    PhaseChanged?.Invoke(_phase);
                }
                break;

            case RoundPhase.BlockTracking:
                _scenario!.Tick(nowS, _yawDeg, _pitchDeg, false);
                if (elapsed >= _blockTrackingS)
                {
                    _winAEndSeq = _ring.Sequence;
                    _winAEndS = nowS;
                    _scenario.SealActiveBlock(nowS);

                    _phase = RoundPhase.BlockFlick;
                    _phaseStartS = nowS;
                    _winBStartSeq = _ring.Sequence;
                    _winBStartS = nowS;
                    _scenario.StartBlock(BlockKind.Flick, nowS, _yawDeg, _pitchDeg);
                    PhaseChanged?.Invoke(_phase);
                }
                break;

            case RoundPhase.BlockFlick:
            {
                var hitTarget = _scenario!.CurrentTargets.Length > 0 ? _scenario.CurrentTargets[0] : (TargetSphere?)null;
                var outcome = _scenario.Tick(nowS, _yawDeg, _pitchDeg, leftEdge);
                if (outcome == TickOutcome.Hit)
                {
                    RecordHit(nowS, hitTarget);
                    _hitFlashUntilS = nowS + 0.09;
                    TargetHit?.Invoke(_scenario.Streak);
                }
                else if (outcome == TickOutcome.Miss)
                {
                    _missFlashUntilS = nowS + 0.12;
                    TargetMissed?.Invoke();
                }

                if (elapsed >= _blockFlickS)
                {
                    _winBEndSeq = _ring.Sequence;
                    _winBEndS = nowS;
                    _scenario.SealActiveBlock(nowS);

                    _phase = RoundPhase.BlockDot;
                    _phaseStartS = nowS;
                    _winCStartSeq = _ring.Sequence;
                    _winCStartS = nowS;
                    _scenario.StartBlock(BlockKind.Dot, nowS, _yawDeg, _pitchDeg);
                    PhaseChanged?.Invoke(_phase);
                }
                break;
            }

            case RoundPhase.BlockDot:
            {
                var hitTarget = _scenario!.CurrentTargets.Length > 0 ? _scenario.CurrentTargets[0] : (TargetSphere?)null;
                var outcome = _scenario.Tick(nowS, _yawDeg, _pitchDeg, leftEdge);
                if (outcome == TickOutcome.Hit)
                {
                    RecordHit(nowS, hitTarget);
                    _hitFlashUntilS = nowS + 0.09;
                    TargetHit?.Invoke(_scenario.Streak);
                }
                else if (outcome == TickOutcome.Miss)
                {
                    _missFlashUntilS = nowS + 0.12;
                    TargetMissed?.Invoke();
                }

                if (elapsed >= _blockDotS)
                {
                    _winCEndSeq = _ring.Sequence;
                    _winCEndS = nowS;
                    _scenario.SealActiveBlock(nowS);
                    if (_isWarmup)
                    {
                        // Warmup is unscored: skip scoring/report entirely.
                        RequestCursorUnlock?.Invoke();
                        _phase = RoundPhase.SessionComplete;
                        PhaseChanged?.Invoke(_phase);
                        WarmupCompleted?.Invoke();
                    }
                    else
                    {
                        EnterRoundScoring(nowS);
                    }
                }
                break;
            }

            case RoundPhase.Rest:
                if (_skipRestRequested || elapsed >= Constants.RestS)
                {
                    _skipRestRequested = false;
                    BeginNextRound(nowS);
                }
                break;
        }
    }

    private void RecordHit(double nowS, TargetSphere? target)
    {
        _lastHitAtS = nowS;
        if (target is { } t)
        {
            _lastHitAzDeg = t.AzimuthDeg;
            _lastHitElDeg = t.ElevationDeg;
        }
    }

    private void PublishViewportState(double nowS)
    {
        var targets = _scenario?.CurrentTargets ?? Array.Empty<TargetSphere>();
        double deltaYaw = 0, deltaPitch = 0;
        if (targets.Length > 0)
        {
            deltaYaw = ScenarioState.SignedYawDeltaDeg(_yawDeg, targets[0].AzimuthDeg);
            deltaPitch = targets[0].ElevationDeg - _pitchDeg;
        }

        ViewportState.Publish(new ViewportState
        {
            YawDeg = _yawDeg,
            PitchDeg = _pitchDeg,
            SampleSeq = _ring.Sequence,
            ProbeCm360 = _currentProbeCm360,
            ActiveBlock = _scenario?.ActiveBlock ?? BlockKind.Tracking,
            Targets = targets,
            HitFlash = nowS < _hitFlashUntilS,
            MissFlash = nowS < _missFlashUntilS,
            StreakCount = _scenario?.Streak ?? 0,
            TargetDeltaYawDeg = deltaYaw,
            TargetDeltaPitchDeg = deltaPitch,
            SamplesPerSec = _samplesPerSec,
            DroppedSamples = Math.Max(0, _input.DroppedSamples - _droppedBaseline),
            LastSwipeCm360 = _lastSwipeCm360,
            AccentR = _scenario?.Variant.AccentR ?? 0.0f,
            AccentG = _scenario?.Variant.AccentG ?? 0.90f,
            AccentB = _scenario?.Variant.AccentB ?? 1.0f,
            LastHitAzimuthDeg = _lastHitAzDeg,
            LastHitElevationDeg = _lastHitElDeg,
            HitRippleAgeS = nowS - _lastHitAtS < 0.24 ? nowS - _lastHitAtS : 99.0,
            HorizontalFovDeg = _horizontalFovDeg
        });
    }

    private void BeginNextRound(double nowS)
    {
        _skipRestRequested = false;
        _retryPending = false;
        _roundIndex++;
        _roundStartS = nowS - _sessionStartS;
        _currentProbeCm360 = _isWarmup
            ? (_session.BoundsLoCm360 + _session.BoundsHiCm360) * 0.5
            : _session.NextProbeCm360();
        _scenario = new ScenarioState(_roundIndex, _sessionSalt);
        _yawDeg = 0;
        _pitchDeg = 0;
        _phase = RoundPhase.RoundIntro;
        _phaseStartS = nowS;
        RequestCursorLock?.Invoke();
        PhaseChanged?.Invoke(_phase);
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

        if (reason == RoundInvalidReason.InsufficientSamples && _invalidRetriesInRound < MaxInvalidRetriesPerRound)
        {
            _invalidRetriesInRound++;
            _retryPending = true;
            _roundIndex--; // keep the same probe and re-run the same round number
            _phase = RoundPhase.Rest;
            _phaseStartS = nowS;
            PhaseChanged?.Invoke(_phase);
            EmitHud(nowS);
            return;
        }

        _invalidRetriesInRound = 0;
        _retryPending = false;

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
        double bsi = roundScore.YNorm * 100.0;
        _isNewBestBsi = bsi > _bestBsi + 0.05;
        if (_isNewBestBsi) _bestBsi = bsi;

        if (_session.CanRunRound && !_session.IsDone)
        {
            _phase = RoundPhase.Rest;
            _phaseStartS = nowS;
            PhaseChanged?.Invoke(_phase);
        }
        else
        {
            _phase = RoundPhase.SessionComplete;
            FinalReport = _session.Report();
            PhaseChanged?.Invoke(_phase);
            Completed?.Invoke(FinalReport, false);
        }
    }

    private (BlockMetrics Metrics, bool Valid) ScoreWindow(
        long seqStart, long seqEnd, double tStartS, double tEndS, IReadOnlyList<Attempt> attempts)
    {
        var samples = _ring.SnapshotRange(seqStart, seqEnd);
        if (samples.Length == 0)
            return (new BlockMetrics(0, 0, 0, Array.Empty<double>(), Array.Empty<double>(), 0, attempts.Count(a => a.Hit), attempts.Count), false);

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
        double lastBsi = _roundScores.Count > 0 ? _roundScores[^1].YNorm * 100.0 : 0;
        double lastAcc = _roundScores.Count > 0 ? _roundScores[^1].Components.Accuracy * 100.0 : 0;

        (string status, double prog, string live) = _phase switch
        {
            RoundPhase.RoundIntro => (
                $"GET READY ({Math.Max(1, (int)Math.Ceiling(_roundIntroS - elapsed))}s)",
                Math.Clamp(elapsed / _roundIntroS, 0, 1),
                "Center your mouse on the pad — Phase 1: Follow sphere (NO CLICK)"),
            RoundPhase.BlockTracking => (
                "PHASE 1/3 · TRACKING (NO CLICK)",
                Math.Clamp(elapsed / _blockTrackingS, 0, 1),
                $"Lock: {_scenario?.LiveTrackingAccuracy * 100:F0}%   ({Math.Max(0, _blockTrackingS - elapsed):F1}s)"),
            RoundPhase.BlockFlick => (
                "PHASE 2/3 · FLICK + LEFT-CLICK",
                Math.Clamp(elapsed / _blockFlickS, 0, 1),
                $"Hits: {_scenario?.FlickAttempts.Count(a => a.Hit) ?? 0}/{_scenario?.FlickAttempts.Count ?? 0}   ({Math.Max(0, _blockFlickS - elapsed):F1}s)"),
            RoundPhase.BlockDot => (
                "PHASE 3/3 · MICRO-DOT + LEFT-CLICK",
                Math.Clamp(elapsed / _blockDotS, 0, 1),
                $"Hits: {_scenario?.DotAttempts.Count(a => a.Hit) ?? 0}/{_scenario?.DotAttempts.Count ?? 0}   ({Math.Max(0, _blockDotS - elapsed):F1}s)"),
            RoundPhase.Rest => _retryPending
                ? (
                    "LOW INPUT DATA — RETRYING ROUND",
                    Math.Clamp(elapsed / Constants.RestS, 0, 1),
                    $"Not enough motion samples. Re-running Round {_roundIndex + 1}. Press [SPACE] to retry now.")
                : (
                    $"ROUND {_roundIndex} COMPLETE · REST ({Math.Max(1, (int)Math.Ceiling(Constants.RestS - elapsed))}s)",
                    Math.Clamp(elapsed / Constants.RestS, 0, 1),
                    $"Round BSI: {lastBsi:F1}  |  Accuracy: {lastAcc:F0}%  —  Press [SPACE] to start Round {_roundIndex + 1}"),
            RoundPhase.SessionComplete => ("SESSION COMPLETE", 1.0, ""),
            RoundPhase.Aborted => ("ABORTED", 0.0, ""),
            _ => ("IDLE", 0.0, "")
        };

        var variant = RoundVariant.ForRound(_roundIndex);
        var next = RoundVariant.ForRound(_roundIndex + 1);
        HudUpdated?.Invoke(new HudSnapshot(
            _phase,
            _roundIndex,
            Constants.MaxRounds,
            status,
            prog,
            live,
            _scenario?.Streak ?? 0,
            lastBsi,
            lastAcc,
            variant.Title,
            variant.Subtitle,
            $"NEXT: ROUND {_roundIndex + 1} · {next.Title} ({next.Subtitle})",
            _bestBsi,
            _isNewBestBsi));
    }
}
