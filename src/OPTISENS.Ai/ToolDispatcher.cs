using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OPTISENS.Core.Projection;

namespace OPTISENS.Ai;

public readonly record struct ToolResultJson(
    [property: JsonPropertyName("cm360")] double Cm360,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("cs2")] double Cs2,
    [property: JsonPropertyName("valorant")] double Valorant,
    [property: JsonPropertyName("ow2")] double Ow2,
    [property: JsonPropertyName("cod")] double Cod);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ToolResultJson))]
internal partial class ToolResultJsonContext : JsonSerializerContext
{
}

public sealed class ToolDispatcher
{
    public static readonly string[] DefaultToolSchemas =
    [
        """{"name": "adjust_sensitivity", "description": "Adjust the recommended sensitivity up or down by a percentage. Use when the player reports the current feel is too fast or too slow.", "parameters": {"type": "object", "properties": {"direction": {"type": "string", "enum": ["faster", "slower"]}, "percentage": {"type": "number"}}, "required": ["direction", "percentage"]}}""",
        """{"name": "set_specific_cm360", "description": "Set the recommended sensitivity to an exact cm/360 value.", "parameters": {"type": "object", "properties": {"cm360": {"type": "number"}}, "required": ["cm360"]}}"""
    ];

    private readonly double _dpi;
    private readonly double _boundsLo;
    private readonly double _boundsHi;

    public double CurrentCm360 { get; private set; }
    public event Action<double>? Cm360Changed;

    public ToolDispatcher(double initialCm360, double dpi, double boundsLoCm360, double boundsHiCm360)
    {
        CurrentCm360 = Math.Clamp(initialCm360, boundsLoCm360, boundsHiCm360);
        _dpi = dpi;
        _boundsLo = boundsLoCm360;
        _boundsHi = boundsHiCm360;
    }

    public string Execute(ToolCallRecord call)
    {
        double next = CurrentCm360;
        string note;

        switch (call.Name)
        {
            case "adjust_sensitivity":
            {
                string dir = GetArg(call, "direction") ?? "";
                if (!TryGetDouble(call, "percentage", out double pct))
                {
                    note = "Missing or invalid 'percentage' argument.";
                    break;
                }
                pct = Math.Clamp(pct, 0, 90);
                if (string.Equals(dir, "faster", StringComparison.OrdinalIgnoreCase))
                {
                    next = CurrentCm360 * (1.0 - pct / 100.0);
                    note = $"Adjusted {pct:F1}% faster.";
                }
                else if (string.Equals(dir, "slower", StringComparison.OrdinalIgnoreCase))
                {
                    next = CurrentCm360 * (1.0 + pct / 100.0);
                    note = $"Adjusted {pct:F1}% slower.";
                }
                else
                {
                    note = $"Unknown direction '{dir}'.";
                }
                break;
            }
            case "set_specific_cm360":
            {
                if (!TryGetDouble(call, "cm360", out double target))
                {
                    note = "Missing or invalid 'cm360' argument.";
                    break;
                }
                next = target;
                note = $"Set cm/360 to {Math.Clamp(next, _boundsLo, _boundsHi):F2}.";
                break;
            }
            default:
                note = $"Unknown tool '{call.Name}'.";
                break;
        }

        CurrentCm360 = Math.Clamp(next, _boundsLo, _boundsHi);
        Cm360Changed?.Invoke(CurrentCm360);

        var res = new ToolResultJson(
            Math.Round(CurrentCm360, 2),
            note,
            Math.Round(GameProjection.ToGameSens(GameEngine.Cs2Source, _dpi, CurrentCm360), 4),
            Math.Round(GameProjection.ToGameSens(GameEngine.ValorantUnreal, _dpi, CurrentCm360), 4),
            Math.Round(GameProjection.ToGameSens(GameEngine.Overwatch2, _dpi, CurrentCm360), 4),
            Math.Round(GameProjection.ToGameSens(GameEngine.CallOfDuty, _dpi, CurrentCm360), 4));

        return JsonSerializer.Serialize(res, ToolResultJsonContext.Default.ToolResultJson);
    }

    private static string? GetArg(ToolCallRecord call, string key)
    {
        foreach (var (k, v) in call.Arguments)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    private static bool TryGetDouble(ToolCallRecord call, string key, out double val)
    {
        val = 0;
        var s = GetArg(call, key);
        return s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val) && double.IsFinite(val);
    }
}
