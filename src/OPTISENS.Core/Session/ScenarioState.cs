using OPTISENS.Core.Config;
using OPTISENS.Core.Kinematics;

namespace OPTISENS.Core.Session;

/// <summary>Pure, deterministic scenario engine for the three 40s calibration blocks.</summary>
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
    private double _targetRadiusDeg;
    private double _targetOnsetS;
    private double _targetExpiryS;
    private double _targetSignedAzimuthDeg;
    private int _flickSign = 1;
    private int _dotsSpawned;
    private double _lastDotYawOffset;
    private double _lastDotPitchOffset;

    public ScenarioState(int roundIndex, int sessionSalt = 1337)
    {
        _rng = new Random(roundIndex * 7919 + sessionSalt);
    }

    public BlockKind ActiveBlock => _activeBlock;
    public TargetSphere[] CurrentTargets { get; private set; } = Array.Empty<TargetSphere>();
    public double LiveTrackingAccuracy => _trackingTicks > 0 ? (double)_trackingOnTargetTicks / _trackingTicks : 0;
    public IReadOnlyList<Attempt> FlickAttempts => _flickAttempts;
    public IReadOnlyList<Attempt> DotAttempts => _dotAttempts;

    public void StartBlock(BlockKind kind, double nowS, double yawDeg, double pitchDeg)
    {
        _activeBlock = kind;
        _blockStartS = nowS;
        _anchorYawDeg = yawDeg;
        _anchorPitchDeg = pitchDeg;
        _hasTarget = false;
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
            SpawnFlickTarget(nowS, yawDeg, pitchDeg);
        }
        else if (kind == BlockKind.Dot)
        {
            _dotAttempts.Clear();
            _dotsSpawned = 0;
            _lastDotYawOffset = 0;
            _lastDotPitchOffset = 0;
            SpawnDotTarget(nowS, yawDeg, pitchDeg);
        }
    }

    public bool Tick(double nowS, double yawDeg, double pitchDeg, bool leftClickEdge)
    {
        bool hitThisTick = false;
        switch (_activeBlock)
        {
            case BlockKind.Tracking:
            {
                double t = nowS - _blockStartS;
                double tx = _anchorYawDeg + Constants.SphereAmpXDeg * Math.Sin(2 * Math.PI * Constants.SphereFreqXHz * t);
                double ty = Math.Clamp(_anchorPitchDeg + Constants.SphereAmpYDeg * Math.Sin(2 * Math.PI * Constants.SphereFreqYHz * t + Constants.SpherePhaseRad), -80, 80);
                double dist = AngularDistanceDeg(yawDeg, pitchDeg, tx, ty);
                _trackingTicks++;
                bool on = dist <= Constants.SphereRadiusDeg + Constants.SphereSlackDeg;
                if (on) _trackingOnTargetTicks++;
                CurrentTargets = [new TargetSphere(tx, ty, Constants.SphereRadiusDeg, on)];
                break;
            }
            case BlockKind.Flick:
            {
                if (!_hasTarget) break;
                double dist = AngularDistanceDeg(yawDeg, pitchDeg, _targetYawDeg, _targetPitchDeg);
                if (leftClickEdge && dist <= _targetRadiusDeg)
                {
                    _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, true));
                    hitThisTick = true;
                    SpawnFlickTarget(nowS, yawDeg, pitchDeg);
                }
                else if (nowS >= _targetExpiryS)
                {
                    _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false));
                    SpawnFlickTarget(nowS, yawDeg, pitchDeg);
                }
                break;
            }
            case BlockKind.Dot:
            {
                if (!_hasTarget) break;
                double dist = AngularDistanceDeg(yawDeg, pitchDeg, _targetYawDeg, _targetPitchDeg);
                if (leftClickEdge && dist <= _targetRadiusDeg)
                {
                    _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, nowS, _targetSignedAzimuthDeg, _targetRadiusDeg, true));
                    hitThisTick = true;
                    SpawnDotTarget(nowS, yawDeg, pitchDeg);
                }
                else if (nowS >= _targetExpiryS)
                {
                    _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false));
                    SpawnDotTarget(nowS, yawDeg, pitchDeg);
                }
                break;
            }
        }
        return hitThisTick;
    }

    public void SealActiveBlock(double nowS)
    {
        if (_hasTarget && _activeBlock == BlockKind.Flick)
        {
            _flickAttempts.Add(new Attempt(BlockKind.Flick, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false));
        }
        else if (_hasTarget && _activeBlock == BlockKind.Dot)
        {
            _dotAttempts.Add(new Attempt(BlockKind.Dot, _targetOnsetS, null, _targetSignedAzimuthDeg, _targetRadiusDeg, false));
        }
        _hasTarget = false;
        CurrentTargets = Array.Empty<TargetSphere>();
    }

    private void SpawnFlickTarget(double nowS, double yawDeg, double pitchDeg)
    {
        double mag = Constants.FlickAzMinDeg + _rng.NextDouble() * (Constants.FlickAzMaxDeg - Constants.FlickAzMinDeg);
        double signedAz = _flickSign * mag;
        _flickSign = -_flickSign;
        double el = (_rng.NextDouble() * 2.0 - 1.0) * Constants.FlickElMaxDeg;

        _targetSignedAzimuthDeg = signedAz;
        _targetYawDeg = WrapYaw(yawDeg + signedAz);
        _targetPitchDeg = Math.Clamp(el, -80, 80);
        _targetRadiusDeg = Constants.FlickTargetRadiusDeg;
        _targetOnsetS = nowS;
        _targetExpiryS = nowS + Constants.FlickTargetLifeS;
        _hasTarget = true;
        CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false)];
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

        _targetSignedAzimuthDeg = ox;
        _targetYawDeg = WrapYaw(yawDeg + ox);
        _targetPitchDeg = Math.Clamp(pitchDeg + oy, -80, 80);
        _targetRadiusDeg = Constants.DotRadiusDeg;
        _targetOnsetS = nowS;
        _targetExpiryS = nowS + Constants.DotLifeS;
        _hasTarget = true;
        CurrentTargets = [new TargetSphere(_targetYawDeg, _targetPitchDeg, _targetRadiusDeg, false)];
    }

    public static double WrapYaw(double yaw)
    {
        double w = yaw % 360.0;
        return w < 0 ? w + 360.0 : w;
    }

    public static double AngularDistanceDeg(double yaw1, double pitch1, double yaw2, double pitch2)
    {
        double dy = ((yaw2 - yaw1 + 540.0) % 360.0) - 180.0;
        double dp = pitch2 - pitch1;
        return Math.Sqrt(dy * dy + dp * dp);
    }
}
