namespace OPTISENS.Core.Kinematics;

public enum BlockKind { Tracking, Flick, Dot }

public readonly record struct Attempt(
    BlockKind BlockKind,
    double TargetOnsetS,
    double? ClickS,
    double SignedTargetAzimuthDeg,   // relative to attempt start yaw
    double TargetRadiusDeg,
    bool Hit);
