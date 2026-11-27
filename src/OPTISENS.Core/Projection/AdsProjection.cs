namespace OPTISENS.Core.Projection;

public sealed record AdsEntry(string Game, string SettingName, string ValueText, string Note);

public static class AdsProjection
{
    /// <summary>Computes zoomed-to-hipfire sensitivity ratio for Monitor Distance coefficient c (0.0 = focal length, 1.0 = 100%, 1.333 = CS2 default).</summary>
    public static double FocalScale(double hipVfvDeg, double zoomVfvDeg, double mdvCoeff = 0.0)
    {
        double th = Math.Tan(hipVfvDeg * 0.5 * Math.PI / 180.0);
        double tz = Math.Tan(zoomVfvDeg * 0.5 * Math.PI / 180.0);
        if (mdvCoeff <= 1e-6) return tz / th;
        return Math.Atan(mdvCoeff * tz) / Math.Atan(mdvCoeff * th);
    }

    /// <summary>Returns recommended ADS/scoped multipliers for major FPS titles at the chosen MDV coefficient (0%, 75%, 100%, 133%).</summary>
    public static IReadOnlyList<AdsEntry> ComputeAll(double mdvPercent)
    {
        double c = Math.Clamp(mdvPercent, 0.0, 200.0) / 100.0;
        // Reference vertical FOVs: CS2 hip 73.74° (90° 4:3) / AWP 1x 30.54° (40° 4:3) -> 0.8189 at 0% MDV, 1.0000 at 133.3% MDV
        double cs2Ratio = FocalScale(73.74, 30.54, c) / FocalScale(73.74, 30.54, 4.0 / 3.0);
        // Valorant hip 70.53° / Operator 2.5x (29.9°) normalized to 1.0 = 1:1 angular
        double valScoped = FocalScale(70.53, 29.90, c) / (29.90 / 70.53);
        // Overwatch 2 hip 70.53° / Widow & Ana 30° -> exact % scale (37.89% at 0% MDV)
        double owWidow = FocalScale(70.53, 30.0, c) * 100.0;
        double owAshe = FocalScale(70.53, 40.0, c) * 100.0;
        // Apex Legends hip 73.74° (90° 4:3) / 1x HCOG 63.0°
        double apex1x = FocalScale(73.74, 63.0, c) / (63.0 / 73.74);

        return
        [
            new("CS2", "zoom_sensitivity_ratio", $"{cs2Ratio:F4}", $"MDV {mdvPercent:F0}%"),
            new("Valorant", "Scoped Multiplier", $"{valScoped:F3}", $"Operator 2.5x · MDV {mdvPercent:F0}%"),
            new("Overwatch 2 (Widow/Ana)", "Relative Aim Zoomed", $"{owWidow:F2}%", $"MDV {mdvPercent:F0}%"),
            new("Overwatch 2 (Ashe)", "Relative Aim Zoomed", $"{owAshe:F2}%", $"MDV {mdvPercent:F0}%"),
            new("Apex Legends", "ADS Scalar (1x)", $"{apex1x:F3}", $"MDV {mdvPercent:F0}%"),
            new("Call of Duty", "Monitor Distance Coeff", $"{c:F2}", "ADS Relative mode"),
        ];
    }
}
