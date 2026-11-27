namespace OPTISENS.Core.Projection;

public enum GameCategory
{
    All,
    TacticalFps,
    HeroShooter,
    BattleRoyale,
    ArenaAndTrainers,
    ExtractionAndMilSim,
    SandboxAndOther
}

public sealed record GameDefinition(
    string Id,
    string Name,
    string ShortName,
    GameCategory Category,
    double YawDeg,
    int Decimals = 4,
    string? ConfigTemplate = null,
    string? Note = null,
    Func<double, double, double>? CustomToSens = null,
    Func<double, double, double>? CustomToCm360 = null)
{
    public double ToGameSens(double dpi, double cm360) =>
        CustomToSens is not null
            ? CustomToSens(dpi, cm360)
            : 360.0 * 2.54 / (dpi * cm360 * YawDeg);

    public double ToCm360(double dpi, double gameSens) =>
        CustomToCm360 is not null
            ? CustomToCm360(dpi, gameSens)
            : 360.0 * 2.54 / (dpi * gameSens * YawDeg);

    public string FormatSens(double sens) =>
        sens.ToString($"F{Decimals}", System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Name;

    public string? FormatConfigCommand(double sens) =>
        ConfigTemplate is null ? null : string.Format(System.Globalization.CultureInfo.InvariantCulture, ConfigTemplate, FormatSens(sens));
}

public static class GameCatalog
{
    private const double PubgYaw = 0.002222222222;

    private static double PubgToSens(double dpi, double cm360) =>
        Math.Clamp(50.0 + 50.0 * Math.Log10(360.0 * 2.54 / (dpi * cm360 * PubgYaw)), 0.0, 100.0);

    private static double PubgToCm360(double dpi, double gameSens) =>
        360.0 * 2.54 / (dpi * PubgYaw * Math.Pow(10.0, (gameSens - 50.0) / 50.0));

    private static double MinecraftToSens(double dpi, double cm360)
    {
        double f = Math.Cbrt(360.0 * 2.54 / (dpi * cm360 * 1.2));
        return (f - 0.2) / 0.6 * 100.0;
    }

    private static double MinecraftToCm360(double dpi, double gameSens)
    {
        double f = 0.6 * (gameSens / 100.0) + 0.2;
        return 360.0 * 2.54 / (dpi * (f * f * f * 1.2));
    }

    public static readonly string[] DefaultPinnedIds = ["cs2", "valorant", "apex", "overwatch2", "cod", "fortnite"];

    public static readonly IReadOnlyList<GameDefinition> All =
    [
        new("cs2", "Counter-Strike 2", "CS2", GameCategory.TacticalFps, 0.022, 4, "sensitivity \"{0}\""),
        new("valorant", "Valorant", "Valorant", GameCategory.TacticalFps, 0.070, 3),
        new("r6siege", "Rainbow Six Siege", "R6 Siege", GameCategory.TacticalFps, 0.00223, 2, Note: "Multiplier 0.02 (default)"),
        new("r6siege_002", "Rainbow Six Siege (0.002)", "R6 (0.002)", GameCategory.TacticalFps, 0.000223, 2, Note: "Multiplier 0.002"),
        new("cod", "Call of Duty (BO6 / MW3 / Warzone)", "CoD", GameCategory.BattleRoyale, 0.0066, 2),
        new("overwatch2", "Overwatch 2", "OW2", GameCategory.HeroShooter, 0.0066, 2),
        new("marvelrivals", "Marvel Rivals", "Rivals", GameCategory.HeroShooter, 0.0175, 3),
        new("apex", "Apex Legends", "Apex", GameCategory.BattleRoyale, 0.022, 4, "mouse_sensitivity \"{0}\""),
        new("fortnite", "Fortnite (Slider %)", "Fortnite", GameCategory.BattleRoyale, 0.005555, 2, Note: "In-game X/Y %"),
        new("pubg", "PUBG: Battlegrounds", "PUBG", GameCategory.BattleRoyale, PubgYaw, 1, Note: "In-game slider (0–100)",
            CustomToSens: PubgToSens, CustomToCm360: PubgToCm360),
        new("thefinals", "The Finals", "The Finals", GameCategory.HeroShooter, 0.00225, 2),
        new("deadlock", "Deadlock", "Deadlock", GameCategory.HeroShooter, 0.044, 3),
        new("tf2", "Team Fortress 2", "TF2", GameCategory.HeroShooter, 0.022, 4, "sensitivity \"{0}\""),
        new("paladins", "Paladins", "Paladins", GameCategory.HeroShooter, 0.005, 2),
        new("destiny2", "Destiny 2", "Destiny 2", GameCategory.HeroShooter, 0.0066, 2),
        new("halo_infinite", "Halo Infinite", "Halo", GameCategory.ArenaAndTrainers, 0.022, 2),
        new("quake_live", "Quake Live / Champions", "Quake", GameCategory.ArenaAndTrainers, 0.022, 4, "sensitivity \"{0}\""),
        new("splitgate", "Splitgate", "Splitgate", GameCategory.ArenaAndTrainers, 0.0055, 3),
        new("aimlab", "Aimlabs (Valorant / Universal)", "Aimlabs", GameCategory.ArenaAndTrainers, 0.070, 3, Note: "Valorant profile"),
        new("kovaaks", "KovaaK's (Quake / Source)", "KovaaK's", GameCategory.ArenaAndTrainers, 0.022, 4, Note: "Quake/Source scale"),
        new("krunker", "Krunker.io", "Krunker", GameCategory.ArenaAndTrainers, 0.066, 3),
        new("bf2042", "Battlefield 2042", "BF2042", GameCategory.ExtractionAndMilSim, 0.00333333, 2, Note: "Uniform Soldier Aiming"),
        new("bfv", "Battlefield V / 1", "BFV", GameCategory.ExtractionAndMilSim, 0.00333333, 2),
        new("tarkov", "Escape from Tarkov", "Tarkov", GameCategory.ExtractionAndMilSim, 0.125, 3, Note: "Unarmored / 0% turn penalty"),
        new("rust", "Rust", "Rust", GameCategory.ExtractionAndMilSim, 0.1125, 3, "input.sensitivity {0}"),
        new("hunt", "Hunt: Showdown 1896", "Hunt", GameCategory.ExtractionAndMilSim, 0.022, 2),
        new("readyornot", "Ready or Not", "RoN", GameCategory.TacticalFps, 0.022, 3),
        new("squad", "Squad", "Squad", GameCategory.ExtractionAndMilSim, 0.01555, 3),
        new("dayz", "DayZ", "DayZ", GameCategory.ExtractionAndMilSim, 0.036, 3),
        new("l4d2", "Left 4 Dead 2", "L4D2", GameCategory.SandboxAndOther, 0.022, 4, "sensitivity \"{0}\""),
        new("doom_eternal", "DOOM Eternal", "DOOM", GameCategory.ArenaAndTrainers, 0.044, 3),
        new("cyberpunk", "Cyberpunk 2077", "Cyberpunk", GameCategory.SandboxAndOther, 0.012, 2),
        new("warframe", "Warframe", "Warframe", GameCategory.SandboxAndOther, 0.0066, 1),
        new("roblox", "Roblox (Default Camera)", "Roblox", GameCategory.SandboxAndOther, 0.09, 3),
        new("minecraft", "Minecraft (Java / Bedrock)", "Minecraft", GameCategory.SandboxAndOther, 1.2, 1, Note: "In-game % (0–200%)",
            CustomToSens: MinecraftToSens, CustomToCm360: MinecraftToCm360),
        new("xdefiant", "XDefiant / Rogue Company", "XDefiant", GameCategory.HeroShooter, 0.0066, 1),
    ];

    public static GameDefinition? FindById(string id) =>
        All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<GameDefinition> Filter(
        string? query, GameCategory category, IReadOnlySet<string> pinnedIds, bool pinnedOnly)
    {
        IEnumerable<GameDefinition> games = All;
        if (category != GameCategory.All)
            games = games.Where(g => g.Category == category);
        if (pinnedOnly)
            games = games.Where(g => pinnedIds.Contains(g.Id));
        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            games = games.Where(g =>
                g.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return games.ToList();
    }
}
