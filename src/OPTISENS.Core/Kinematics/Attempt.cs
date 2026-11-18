namespace OPTISENS.Core.Kinematics;

public enum BlockKind { Tracking, Flick, Dot }

public readonly record struct Attempt(
    BlockKind BlockKind,
    double TargetOnsetS,
    double? ClickS,                 // null if target expired unclicked
    double SignedTargetAzimuthDeg,  // signed horizontal delta from aim-at-onset to target center
    double TargetRadiusDeg,
    bool Hit,
    double? WindowEndS = null);     // end of attempt window (click or expiry)
