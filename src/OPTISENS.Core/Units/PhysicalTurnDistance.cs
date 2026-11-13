namespace OPTISENS.Core.Units;

/// <summary>Invariant sensitivity: centimeters of mouse travel per 360° turn.
/// Value semantics; always positive; constructed only via validated factories.</summary>
public readonly record struct PhysicalTurnDistance
{
    public double Cm360 { get; }

    private PhysicalTurnDistance(double cm360) => Cm360 = cm360;

    public static PhysicalTurnDistance FromCm(double cm360)
    {
        if (!double.IsFinite(cm360) || cm360 <= 0)
            throw new ArgumentOutOfRangeException(nameof(cm360), cm360, "cm/360 must be finite and > 0");
        return new PhysicalTurnDistance(cm360);
    }

    public double CountsPer360(double dpi) => Cm360 * dpi / 2.54;
    public double DegPerCount(double dpi) => 360.0 * 2.54 / (dpi * Cm360);
    public double RadPerCount(double dpi) => DegPerCount(dpi) * (Math.PI / 180.0);

    /// <summary>Centimeters of travel for a raw count delta (sign preserved).</summary>
    public double CountsToCm(double counts, double dpi) => counts * 2.54 / dpi;

    public override string ToString() => Cm360.ToString("F2") + " cm/360";
}
