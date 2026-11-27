using OPTISENS.Core.Input;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Units;

namespace OPTISENS.Core.Session;

public readonly record struct TargetSphere(
    double AzimuthDeg,
    double ElevationDeg,
    double RadiusDeg,
    bool IsHit,
    double LifeRemaining01 = 1.0,
    double SpawnAgeS = 1.0,
    bool HasTether = false,
    double PrevAzimuthDeg = 0.0,
    double PrevElevationDeg = 0.0);

public sealed class ViewportState
{
    private static ViewportState _current = new();
    public static ViewportState Snapshot => Volatile.Read(ref _current);

    public double YawDeg { get; init; }
    public double PitchDeg { get; init; }
    public long SampleSeq { get; init; }
    public double ProbeCm360 { get; init; }
    public BlockKind ActiveBlock { get; init; }
    public TargetSphere[] Targets { get; init; } = Array.Empty<TargetSphere>();
    public bool HitFlash { get; init; }
    public bool MissFlash { get; init; }
    public int StreakCount { get; init; }
    public double TargetDeltaYawDeg { get; init; }
    public double TargetDeltaPitchDeg { get; init; }
    public double SamplesPerSec { get; init; }
    public long DroppedSamples { get; init; }
    public double LastSwipeCm360 { get; init; }
    public float AccentR { get; init; } = 0.0f;
    public float AccentG { get; init; } = 0.90f;
    public float AccentB { get; init; } = 1.0f;
    public double LastHitAzimuthDeg { get; init; }
    public double LastHitElevationDeg { get; init; }
    public double HitRippleAgeS { get; init; } = 99.0;

    public static void Publish(ViewportState next) => Interlocked.Exchange(ref _current, next);

    public static (double YawDeg, double PitchDeg) ApplySample(
        double yawDeg, double pitchDeg, in RawMotionSample s, double dpi, double cm360)
    {
        double degPerCount = PhysicalTurnDistance.FromCm(cm360).DegPerCount(dpi);
        double newYaw = (yawDeg + s.DxCounts * degPerCount) % 360.0;
        if (newYaw < 0) newYaw += 360.0;
        double newPitch = Math.Clamp(pitchDeg - s.DyCounts * degPerCount, -89.0, 89.0);
        return (newYaw, newPitch);
    }
}
