using OPTISENS.Core.Input;
using OPTISENS.Core.Units;

namespace OPTISENS.Core.Session;

public readonly record struct TargetSphere(double AzimuthDeg, double ElevationDeg, double RadiusDeg, bool IsHit);

public sealed class ViewportState
{
    private static ViewportState _current = new();
    public static ViewportState Snapshot => Volatile.Read(ref _current);

    public double YawDeg { get; init; }
    public double PitchDeg { get; init; }
    public long SampleSeq { get; init; }
    public double ProbeCm360 { get; init; }
    public TargetSphere[] Targets { get; init; } = Array.Empty<TargetSphere>();
    public bool HitFlash { get; init; }
    public double SamplesPerSec { get; init; }
    public long DroppedSamples { get; init; }
    public double LastSwipeCm360 { get; init; }

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
