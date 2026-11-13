namespace OPTISENS.Core.Projection;

public enum GameEngine { Cs2Source = 0, ValorantUnreal = 1, Overwatch2 = 2, CallOfDuty = 3 }

public static class GameProjection
{
    // Degrees per count per 1.0 game-sens unit. See §3.2 table.
    public static double YawDeg(GameEngine e) => e switch
    {
        GameEngine.Cs2Source      => 0.022,
        GameEngine.ValorantUnreal => 0.070,
        GameEngine.Overwatch2     => 0.0066,
        GameEngine.CallOfDuty     => 0.0066,
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    /// <summary>gameSens = 360*2.54 / (DPI * cm360 * yaw).</summary>
    public static double ToGameSens(GameEngine e, double dpi, double cm360)
        => 360.0 * 2.54 / (dpi * cm360 * YawDeg(e));

    /// <summary>cm360 = 360*2.54 / (DPI * gameSens * yaw).</summary>
    public static double ToCm360(GameEngine e, double dpi, double gameSens)
        => 360.0 * 2.54 / (dpi * gameSens * YawDeg(e));
}
