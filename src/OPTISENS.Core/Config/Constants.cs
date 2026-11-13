namespace OPTISENS.Core.Config;

/// <summary>All tunables in one place. [TUNABLE] values may be adjusted with DEVLOG entries; the rest are contractual.</summary>
public static class Constants
{
    // --- Input & trajectory ---
    public const int    SampleRingCapacity   = 65_536;
    public const double ResampleDtS          = 0.001;
    public const int    MinBlockSamples      = 100;      // <100 ms of data -> round invalid

    // --- Kinematics (§5.3) ---
    public const double JRefDegPerS3            = 10_000.0; // [TUNABLE] "poor" end of jerk normalization
    public const double ZeroVelThresholdDegPerS = 5.0;      // [TUNABLE]
    public const int    ZeroVelDwellMs          = 30;       // [TUNABLE]
    public const double TAdjustRefMs            = 500.0;    // [TUNABLE]
    public const double EFlickClipLo            = -1.0;
    public const double EFlickClipHi            = 2.0;

    // --- BSI (§6, D13) ---
    public const double W1 = 0.40;                       // [TUNABLE] accuracy
    public const double W2 = 0.25;                       // [TUNABLE] 1 - normJerk
    public const double W3 = 0.20;                       // [TUNABLE] 1 - |E_flick|
    public const double W4 = 0.15;                       // [TUNABLE] 1 - T_adjust

    // --- Optimizer (§7, D14) ---
    public const double BoundsMinCm360      = 10.0;      // [TUNABLE]
    public const double BoundsMaxCm360      = 60.0;      // [TUNABLE]
    public const double PadTurnFraction     = 2.0;       // hi = min(max, 2 * pad width)
    public const double MaternLengthScale   = 0.25;      // in normalized units
    public const double GpNoiseFloor        = 0.05;      // sigma_n^2 on y/100 scale
    public const double EiXi                = 0.01;
    public const int    EiGridPoints        = 150;
    public const double ConvergenceEps      = 0.05;      // max-sigma stop
    public const double RecommendBandEps    = 0.02;      // mu* - band cutoff
    public const double FatigueLambda       = 0.05;      // per-minute noise inflation
    public const int    MaxRounds           = 6;

    // --- Scenarios (§9.4) & round timing (§9.5) ---
    public const double BlockTrackingS       = 15.0;
    public const double BlockFlickS          = 15.0;
    public const double BlockDotS            = 10.0;
    public const double RoundIntroS          = 3.0;
    public const double RestS                = 10.0;
    public const double SphereRadiusDeg      = 3.5;
    public const double SphereSlackDeg       = 1.0;
    public const double SphereAmpXDeg        = 14.0;
    public const double SphereAmpYDeg        = 9.0;
    public const double SphereFreqXHz        = 0.10;
    public const double SphereFreqYHz        = 0.07;
    public const double SpherePhaseRad       = Math.PI / 3.0;
    public const double FlickAzMinDeg        = 90.0;
    public const double FlickAzMaxDeg        = 120.0;
    public const double FlickElMaxDeg        = 8.0;
    public const double FlickTargetRadiusDeg = 3.0;
    public const double FlickTargetLifeS     = 2.4;
    public const double DotWindowDeg         = 10.0;
    public const double DotMinSepDeg         = 2.5;
    public const double DotRadiusDeg         = 0.8;
    public const double DotLifeS             = 1.8;
    public const int    DotCount             = 10;

    // --- AI (§8, D17) ---
    public const double AiTemperature       = 0.15;      // [TUNABLE]
    public const int    AiTopK              = 50;
    public const double AiRepetitionPenalty = 1.1;
    public const int    AiMaxNewTokens      = 512;
    public const int    MaxToolRoundTrips   = 3;

    // --- Profile validation (§12 M5) ---
    public const int    DpiMin = 100, DpiMax = 6400, DpiDefault = 800;
    public const double PadWidthMinCm = 5.0, PadWidthDefaultCm = 45.0;
}
