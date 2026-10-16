using LocalXI.Core.Config;

namespace LocalXI.Core.Server;

/// <summary>
/// Maps a <see cref="GameEra"/> onto LandSandBoat's own era support (see its modules/README.md):
/// expansion settings, the self-gating era/lua rule modules, and the era/sql data packs.
/// </summary>
public static class EraPresets
{
    public const string ModuleListFile = "modules/init.txt";

    // era/sql/<tag> holds data changes that revert what expansion <tag> changed, so a classic world applies the
    // packs of every expansion after Wings of the Goddess. Folders missing from a server version are skipped.
    private static readonly string[] AfterWingsOfTheGoddess = ["abyssea", "voidwatch", "soa", "rov", "tvr"];

    public static string Describe(GameEra era) => era switch
    {
        GameEra.Classic75 => "Classic (level 75, up to Wings of the Goddess)",
        _ => "Modern (level 99, every expansion)",
    };

    public static string ShortName(GameEra era) => era == GameEra.Classic75 ? "Classic era, level 75" : "Modern, level 99";

    /// <summary>
    /// Server settings as XI_MAIN_* environment variables. All values are numbers, which LandSandBoat's
    /// environment overrides support. Modern returns nothing and leaves LandSandBoat's retail defaults alone.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Settings(GameEra era)
    {
        if (era != GameEra.Classic75)
        {
            return new Dictionary<string, string>();
        }

        var on = new[] { "ROTZ", "COP", "TOAU", "WOTG", "ACP", "AMK", "ASA" };
        var off = new[]
        {
            "ABYSSEA", "VOIDWATCH", "SOA", "ROV", "TVR",
            "MOG_GARDEN", "MOG_HOUSE_2F", "ROE", "ROE_TIMED", "VOIDWALKER", "DAILY_TALLY",
        };

        var settings = new Dictionary<string, string>
        {
            // Only load content tagged for the enabled expansions, instead of merely hiding later content.
            ["XI_MAIN_RESTRICT_CONTENT"] = "1",
            ["XI_MAIN_MAX_LEVEL"] = "75",
        };
        foreach (var tag in on)
        {
            settings[$"XI_MAIN_ENABLE_{tag}"] = "1";
        }

        foreach (var tag in off)
        {
            settings[$"XI_MAIN_ENABLE_{tag}"] = "0";
        }

        return settings;
    }

    public static IReadOnlyList<string> SqlPacks(GameEra era) => era == GameEra.Classic75 ? AfterWingsOfTheGoddess : [];

    /// <summary>
    /// Whether the map server may load zones on demand (--lazy). Not in classic mode: many era/lua overrides target
    /// zone scripts (notorious monster respawns, shop stock, fame checks), which only exist once their zone is loaded,
    /// so with lazy zones they are never applied (verified against LandSandBoat 1f204df: 114 overrides skipped).
    /// </summary>
    public static bool AllowsLazyZones(GameEra era) => era != GameEra.Classic75;

    /// <summary>
    /// The modules LandSandBoat loads at start-up: era/lua and LocalXI's own module, when the server has them. era/lua
    /// is always listed: each of its overrides only applies when the expansion it reverts is disabled, so in a modern
    /// world it does nothing.
    /// </summary>
    public static void WriteModuleList(string serverDir)
    {
        var modules = Path.Combine(serverDir, "modules");
        var entries = new[] { "era/lua", LocalXiModule.Folder }.Where(e => Directory.Exists(Path.Combine(modules, e))).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        File.WriteAllLines(Path.Combine(serverDir, ModuleListFile),
        [
            "# Written by LocalXI each time the server starts; changes here are overwritten.",
            "# era/lua overrides switch themselves on and off from the ENABLE_* expansion settings.",
            .. entries,
            "",
        ]);
    }
}
