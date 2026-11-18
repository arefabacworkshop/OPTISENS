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
    private int _dotsSpawned;
    private double _lastDotYawOffset;
    private double _lastDotPitchOffset;
    private int _streak;

    public ScenarioState(int roundIndex, int sessionSalt = 1337)
    {
        _rng = new Random(roundIndex * 7919 + sessionSalt);
    }

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
                // Smooth multi-frequency strafe curve (primary + secondary harmonic for reactive reversals)
                double rawYawOffset = Constants.SphereAmpXDeg * (
                    0.75 * Math.Sin(2 * Math.PI * Constants.SphereFreqXHz * t) +
                    0.25 * Math.Sin(2 * Math.PI * (Constants.SphereFreqXHz * 2.3) * t));
                double tx = WrapYaw(_anchorYawDeg + rawYawOffset);
                double ty = Math.Clamp(
                    _anchorPitchDeg + Constants.SphereAmpYDeg * Math.Sin(2 * Math.PI * Constants.SphereFreqYHz * t + Constants.SpherePhaseRad),
                    -75.0, 75.0);

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

    public void SealActiveBlock(double nowS)
    {
        if (_hasTarget && _activeBlock == BlockKind.Flick)
        {
            _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
        }
        else if (_hasTarget && _activeBlock == BlockKind.Dot)
        {
            _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false, nowS));
        }
        _hasTarget = false;
        CurrentTargets = Array.Empty<TargetSphere>();
    }

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
        double signedAz = _flickSign * mag;
        _flickSign = -_flickSign;
        double el = (_rng.NextDouble() * 2.0 - 1.0) * Constants.FlickElMaxDeg;

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
            ox = (_rng.NextDouble() * 2.0 - 1.0) * half;
            oy = (_rng.NextDouble() * 2.0 - 1.0) * half;
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
        double dy = SignedYawDeltaDeg(yaw1, yaw2);
        double dp = pitch2 - pitch1;
        return Math.Sqrt(dy * dy + dp * dp);
    }
}
