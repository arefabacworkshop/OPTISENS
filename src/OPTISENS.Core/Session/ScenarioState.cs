using OPTISENS.Core.Config;
using OPTISENS.Core.Kinematics;

namespace OPTISENS.Core.Session;

public enum TickOutcome { None, Hit, Miss }

/// <summary>Pure, deterministic scenario engine for the three calibration blocks.</summary>
public sealed class ScenarioState
{
    private readonly Random _rng;
    private readonly List<Attempt> _flickAttempts = new();
    private readonly List<Attempt> _dotAttempts = new();

    private BlockKind _activeBlock = BlockKind.Tracking;
    private double _blockStartS;
    private double _anchorYawDeg;
    private double _anchorPitchDeg;

    // Block A (Tracking)
    private int _trackingTicks;
    private int _trackingOnTargetTicks;

    // Block B (Flick) & Block C (Dot) active target
    private bool _hasTarget;
    private double _targetYawDeg;
    private double _targetPitchDeg;
    private double _prevTargetYawDeg;
    private double _prevTargetPitchDeg;
    private bool _hasPrevTarget;
    private double _targetRadiusDeg;
    private double _targetOnsetS;
    private double _targetExpiryS;
    private double _targetSignedAzimuthDeg;
    private int _flickSign = 1;
    private int _flickStep;
    private int _cascadeCount;
    private int _cascadeDirection = 1;
    private int _dotsSpawned;
    private double _lastDotYawOffset;
    private double _lastDotPitchOffset;
    private int _streak;

    public ScenarioState(int roundIndex, int sessionSalt = 1337)
    {
        Variant = RoundVariant.ForRound(roundIndex);
        _rng = new Random(roundIndex * 7919 + sessionSalt);
    }

    public RoundVariant Variant { get; }
    public BlockKind ActiveBlock => _activeBlock;
    public TargetSphere[] CurrentTargets { get; private set; } = Array.Empty<TargetSphere>();
    public double LiveTrackingAccuracy => _trackingTicks > 0 ? (double)_trackingOnTargetTicks / _trackingTicks : 0;
    public IReadOnlyList<Attempt> FlickAttempts => _flickAttempts;
    public IReadOnlyList<Attempt> DotAttempts => _dotAttempts;
    public int Streak => _streak;

    public void StartBlock(BlockKind kind, double nowS, double yawDeg, double pitchDeg)
    {
        _activeBlock = kind;
        _blockStartS = nowS;
        _anchorYawDeg = WrapYaw(yawDeg);
        _anchorPitchDeg = Math.Clamp(pitchDeg, -45.0, 45.0);
        _hasTarget = false;
        _hasPrevTarget = false;
        _streak = 0;
        CurrentTargets = Array.Empty<TargetSphere>();

        if (kind == BlockKind.Tracking)
        {
            _trackingTicks = 0;
            _trackingOnTargetTicks = 0;
        }
        else if (kind == BlockKind.Flick)
        {
            _flickAttempts.Clear();
            _flickSign = 1;
            _flickStep = 0;
            _cascadeCount = 0;
            _cascadeDirection = 1;
            _prevTargetYawDeg = _anchorYawDeg;
            _prevTargetPitchDeg = _anchorPitchDeg;
            SpawnFlickTarget(nowS, yawDeg, pitchDeg);
        }
        else if (kind == BlockKind.Dot)
        {
            _dotAttempts.Clear();
            _dotsSpawned = 0;
            _lastDotYawOffset = 0;
            _lastDotPitchOffset = 0;
            _prevTargetYawDeg = _anchorYawDeg;
            _prevTargetPitchDeg = _anchorPitchDeg;
            SpawnDotTarget(nowS, yawDeg, pitchDeg);
        }
    }

    public TickOutcome Tick(double nowS, double yawDeg, double pitchDeg, bool leftClickEdge)
    {
        TickOutcome outcome = TickOutcome.None;
        switch (_activeBlock)
        {
            case BlockKind.Tracking:
            {
                double t = nowS - _blockStartS;
                var (yawOffset, pitchOffset) = ComputeTrackingOffset(Variant.Tracking, t);
                double tx = WrapYaw(_anchorYawDeg + yawOffset);
                double ty = Math.Clamp(_anchorPitchDeg + pitchOffset, -75.0, 75.0);

                double dist = AngularDistanceDeg(yawDeg, pitchDeg, tx, ty);
                _trackingTicks++;
                bool on = dist <= Constants.SphereRadiusDeg + Constants.SphereSlackDeg;
                if (on)
                {
                    _trackingOnTargetTicks++;
                    _streak = Math.Min(_streak + 1, 999);
                }
                else
                {
                    _streak = 0;
                }

                double prevTx = CurrentTargets.Length > 0 ? CurrentTargets[0].AzimuthDeg : tx;
                double prevTy = CurrentTargets.Length > 0 ? CurrentTargets[0].ElevationDeg : ty;
                double life01 = Math.Clamp(1.0 - (t / Constants.BlockTrackingS), 0.0, 1.0);
                CurrentTargets = [new TargetSphere(tx, ty, Constants.SphereRadiusDeg, on, life01, t, true, prevTx, prevTy)];
                break;
            }
            case BlockKind.Flick:
            {
                if (!_hasTarget) break;
                double dist = AngularDistanceDeg(yawDeg, pitchDeg, _targetYawDeg, _targetPitchDeg);
                double age = nowS - _targetOnsetS;
                double life01 = Math.Clamp((_targetExpiryS - nowS) / Constants.FlickTargetLifeS, 0.0, 1.0);

                if (leftClickEdge && dist <= _targetRadiusDeg)
                {
                    _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, true, nowS));
                    _streak++;
                    outcome = TickOutcome.Hit;
                    SpawnFlickTarget(nowS, yawDeg, pitchDeg);
                }
                else if (leftClickEdge && age >= Constants.MissClickCooldownS && dist <= _targetRadiusDeg * 6.0)
                {
                    // Intentional near-target miss click: penalize accuracy and advance target
                    _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
                    _streak = 0;
                    outcome = TickOutcome.Miss;
                    SpawnFlickTarget(nowS, yawDeg, pitchDeg);
                }
                else if (nowS >= _targetExpiryS)
                {
                    _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
                    _streak = 0;
                    outcome = TickOutcome.Miss;
                    SpawnFlickTarget(nowS, yawDeg, pitchDeg);
                }
                else
                {
                    CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false, life01, age, _hasPrevTarget, _prevTargetYawDeg, _prevTargetPitchDeg)];
                }
                break;
            }
            case BlockKind.Dot:
            {
                if (!_hasTarget) break;
                double dist = AngularDistanceDeg(yawDeg, pitchDeg, _targetYawDeg, _targetPitchDeg);
                double age = nowS - _targetOnsetS;
                double life01 = Math.Clamp((_targetExpiryS - nowS) / Constants.DotLifeS, 0.0, 1.0);

                if (leftClickEdge && dist <= _targetRadiusDeg)
                {
                    _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, true, nowS));
                    _streak++;
                    outcome = TickOutcome.Hit;
                    SpawnDotTarget(nowS, yawDeg, pitchDeg);
                }
                else if (leftClickEdge && age >= Constants.MissClickCooldownS && dist <= Constants.DotWindowDeg)
                {
                    _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
                    _streak = 0;
                    outcome = TickOutcome.Miss;
                    SpawnDotTarget(nowS, yawDeg, pitchDeg);
                }
                else if (nowS >= _targetExpiryS)
                {
                    _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
                    _streak = 0;
                    outcome = TickOutcome.Miss;
                    SpawnDotTarget(nowS, yawDeg, pitchDeg);
                }
                else
                {
                    CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false, life01, age, _hasPrevTarget, _prevTargetYawDeg, _prevTargetPitchDeg)];
                }
                break;
            }
        }
        return outcome;
    }

    /// <summary>Clears the active target at block end without recording an unearned miss.</summary>
    public void SealActiveBlock(double nowS)
    {
        _hasTarget = false;
        CurrentTargets = Array.Empty<TargetSphere>();
    }

    /// <summary>Shifts all live timestamps forward so targets and curves survive a pause.</summary>
    public void ShiftTime(double deltaS)
    {
        if (deltaS <= 0) return;
        _blockStartS += deltaS;
        if (_hasTarget)
        {
            _targetOnsetS += deltaS;
            _targetExpiryS += deltaS;
        }
    }

    /// <summary>Iso-speed tracking curves: all stay within ±20° yaw / ±12° pitch.</summary>
    public static (double Yaw, double Pitch) ComputeTrackingOffset(TrackingPattern pattern, double t) => pattern switch
    {
        TrackingPattern.HorizonStrafe => (
            Constants.SphereAmpXDeg * (0.75 * Math.Sin(2 * Math.PI * 0.18 * t) + 0.25 * Math.Sin(2 * Math.PI * 0.414 * t)),
            Constants.SphereAmpYDeg * Math.Sin(2 * Math.PI * 0.13 * t + Math.PI / 3.0)),
        TrackingPattern.FigureEightFlow => (
            Constants.SphereAmpXDeg * Math.Sin(2 * Math.PI * 0.17 * t),
            9.0 * Math.Sin(4 * Math.PI * 0.17 * t)),
        TrackingPattern.DiagonalGlide => (
            17.0 * Math.Cos(2 * Math.PI * 0.16 * t),
            10.0 * Math.Sin(2 * Math.PI * 0.16 * t + Math.PI / 6.0) + 3.0 * Math.Cos(2 * Math.PI * 0.32 * t)),
        TrackingPattern.ReactiveWeave => (
            Constants.SphereAmpXDeg * (0.65 * Math.Sin(2 * Math.PI * 0.19 * t) + 0.35 * Math.Cos(2 * Math.PI * 0.37 * t)),
            8.0 * Math.Sin(2 * Math.PI * 0.14 * t)),
        TrackingPattern.PendulumArc => (
            19.0 * Math.Sin(2 * Math.PI * 0.17 * t),
            -8.0 * Math.Cos(4 * Math.PI * 0.17 * t) + 2.0),
        _ => (
            17.0 * (0.7 * Math.Sin(2 * Math.PI * 0.18 * t) + 0.3 * Math.Sin(2 * Math.PI * 0.34 * t + 0.8)),
            9.0 * (0.75 * Math.Cos(2 * Math.PI * 0.15 * t) + 0.25 * Math.Sin(2 * Math.PI * 0.30 * t)))
    };

    private void SpawnFlickTarget(double nowS, double yawDeg, double pitchDeg)
    {
        if (_hasTarget)
        {
            _prevTargetYawDeg = _targetYawDeg;
            _prevTargetPitchDeg = _targetPitchDeg;
            _hasPrevTarget = true;
        }
        else
        {
            _prevTargetYawDeg = WrapYaw(yawDeg);
            _prevTargetPitchDeg = pitchDeg;
            _hasPrevTarget = true;
        }

        double mag = Constants.FlickAzMinDeg + _rng.NextDouble() * (Constants.FlickAzMaxDeg - Constants.FlickAzMinDeg);
        double signedAz;
        double el;
        _flickStep++;

        switch (Variant.Flick)
        {
            case FlickPattern.DiagonalCross:
                signedAz = _flickSign * mag * 0.92;
                el = _flickSign * (5.0 + _rng.NextDouble() * 6.0);
                _flickSign = -_flickSign;
                break;
            case FlickPattern.CenterReturnHub:
                double toAnchor = SignedYawDeltaDeg(yawDeg, WrapYaw(_anchorYawDeg));
                if (_flickSign > 0)
                {
                    signedAz = mag;
                    _flickSign = -1;
                }
                else
                {
                    signedAz = Math.Abs(toAnchor) >= 20.0 ? toAnchor : -mag;
                    _flickSign = 1;
                }
                el = (_rng.NextDouble() * 2.0 - 1.0) * 4.0;
                break;
            case FlickPattern.CompassStar:
                double theta = 2.0 * Math.PI * (_flickStep % 8) / 8.0;
                signedAz = Math.Cos(theta) * mag;
                el = Math.Clamp(Math.Sin(theta) * 11.0, -Constants.FlickElMaxDeg - 1.0, Constants.FlickElMaxDeg + 1.0);
                break;
            case FlickPattern.DoubleStepCascade:
                if (_cascadeCount >= 2)
                {
                    _cascadeDirection = -_cascadeDirection;
                    _cascadeCount = 0;
                }
                signedAz = _cascadeDirection * (24.0 + _rng.NextDouble() * 12.0);
                _cascadeCount++;
                el = (_rng.NextDouble() * 2.0 - 1.0) * 6.0;
                break;
            case FlickPattern.DynamicMix:
                signedAz = _flickSign > 0 ? mag : -mag * 0.92;
                el = _flickSign > 0
                    ? (_rng.NextDouble() * 2.0 - 1.0) * 6.0
                    : -(5.0 + _rng.NextDouble() * 6.0);
                _flickSign = -_flickSign;
                break;
            default:
                signedAz = _flickSign * mag;
                _flickSign = -_flickSign;
                el = (_rng.NextDouble() * 2.0 - 1.0) * Constants.FlickElMaxDeg;
                break;
        }

        _targetSignedAzimuthDeg = signedAz;
        _targetYawDeg = WrapYaw(yawDeg + signedAz);
        _targetPitchDeg = Math.Clamp(el, -75.0, 75.0);
        _targetRadiusDeg = Constants.FlickTargetRadiusDeg;
        _targetOnsetS = nowS;
        _targetExpiryS = nowS + Constants.FlickTargetLifeS;
        _hasTarget = true;
        CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false, 1.0, 0.0, _hasPrevTarget, _prevTargetYawDeg, _prevTargetPitchDeg)];
    }

    private void SpawnDotTarget(double nowS, double yawDeg, double pitchDeg)
    {
        if (_dotsSpawned >= Constants.DotCount)
        {
            _hasTarget = false;
            CurrentTargets = Array.Empty<TargetSphere>();
            return;
        }
        _dotsSpawned++;

        if (_hasTarget)
        {
            _prevTargetYawDeg = _targetYawDeg;
            _prevTargetPitchDeg = _targetPitchDeg;
            _hasPrevTarget = true;
        }
        else
        {
            _prevTargetYawDeg = WrapYaw(yawDeg);
            _prevTargetPitchDeg = pitchDeg;
            _hasPrevTarget = true;
        }

        double half = Constants.DotWindowDeg / 2.0;
        double ox = 0, oy = 0;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            (ox, oy) = SampleDotOffset(Variant.Dot, half, _dotsSpawned);
            double dx = ox - _lastDotYawOffset;
            double dy = oy - _lastDotPitchOffset;
            if (Math.Sqrt(dx * dx + dy * dy) >= Constants.DotMinSepDeg) break;
        }
        _lastDotYawOffset = ox;
        _lastDotPitchOffset = oy;

        double targetYaw = WrapYaw(_anchorYawDeg + ox);
        double targetPitch = Math.Clamp(_anchorPitchDeg + oy, -75.0, 75.0);
        _targetSignedAzimuthDeg = SignedYawDeltaDeg(yawDeg, targetYaw);
        _targetYawDeg = targetYaw;
        _targetPitchDeg = targetPitch;
        _targetRadiusDeg = Constants.DotRadiusDeg;
        _targetOnsetS = nowS;
        _targetExpiryS = nowS + Constants.DotLifeS;
        _hasTarget = true;
        CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false, 1.0, 0.0, _hasPrevTarget, _prevTargetYawDeg, _prevTargetPitchDeg)];
    }

    private (double X, double Y) SampleDotOffset(DotPattern pattern, double half, int index)
    {
        switch (pattern)
        {
            case DotPattern.RingCluster:
            {
                double a = 2.0 * Math.PI * (index % 4) / 4.0;
                return (Math.Cos(a) * 4.5, Math.Sin(a) * 4.5);
            }
            case DotPattern.HeadLevelLine:
                return ((_rng.NextDouble() * 2.0 - 1.0) * half, (_rng.NextDouble() * 2.0 - 1.0) * 1.2);
            case DotPattern.CrosshairDiamond:
            {
                double a = Math.PI * (index % 8) / 4.0;
                double r = 3.5 + (index % 2) * 2.0;
                return (Math.Cos(a) * r, Math.Sin(a) * r);
            }
            case DotPattern.AscendingArc:
            {
                double x = (_rng.NextDouble() * 2.0 - 1.0) * 5.5;
                return (x, 3.5 * Math.Sin(x / 5.5 * Math.PI / 2.0) + (_rng.NextDouble() * 2.0 - 1.0));
            }
            case DotPattern.FinaleCluster:
                return ((_rng.NextDouble() * 2.0 - 1.0) * 5.5, (_rng.NextDouble() * 2.0 - 1.0) * 2.5);
            default:
                return ((_rng.NextDouble() * 2.0 - 1.0) * half, (_rng.NextDouble() * 2.0 - 1.0) * half);
        }
    }

    public static double WrapYaw(double yaw)
    {
        double w = yaw % 360.0;
        return w < 0 ? w + 360.0 : w;
    }

    public static double SignedYawDeltaDeg(double fromYaw, double toYaw)
    {
        double diff = (toYaw - fromYaw) % 360.0;
        if (diff > 180.0) diff -= 360.0;
        else if (diff < -180.0) diff += 360.0;
        return diff;
    }

    public static double AngularDistanceDeg(double yaw1, double pitch1, double yaw2, double pitch2)
    {
        double dy = SignedYawDeltaDeg(yaw1, yaw2) * Math.Cos(((pitch1 + pitch2) * 0.5) * Math.PI / 180.0);
        double dp = pitch2 - pitch1;
        return Math.Sqrt(dy * dy + dp * dp);
    }
}
