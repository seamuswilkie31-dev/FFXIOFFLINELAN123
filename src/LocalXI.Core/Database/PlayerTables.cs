using System.Text.RegularExpressions;

namespace LocalXI.Core.Database;

/// <summary>
/// The tables that hold player progress (accounts, characters, inventory, quests, missions, linkshells...).
/// Everything else in the database is game data that a server update can safely re-import.
/// </summary>
public static partial class PlayerTables
{
    /// <summary>Mirrors <c>player_data</c> in LandSandBoat's tools/dbtool.py as of October 2026.</summary>
    public static readonly IReadOnlyList<string> Fallback =
    [
        "accounts", "accounts_banned", "accounts_files", "accounts_friends", "accounts_messages", "accounts_profile",
        "accounts_totp", "auction_house_items", "auction_house", "audit_bazaar", "audit_dbox", "audit_trade",
        "audit_vendor", "char_blacklist", "char_chocobos", "char_effects", "char_equip", "char_equip_saved", "char_exp",
        "char_fishing_contest_history", "char_flags", "char_history", "char_inventory", "char_jobs", "char_job_points",
        "char_look", "char_merit", "char_monstrosity", "char_pet", "char_points", "char_profile", "char_skills",
        "char_spells", "char_stats", "char_storage", "char_style", "char_unlocks", "char_vars", "chars",
        "chocobo_race_results", "conquest_system", "delivery_box", "fishing_contest", "fishing_contest_entries",
        "help_desk", "ip_exceptions", "linkshell_concierge", "linkshells", "server_variables", "unity_system",
    ];

    /// <summary>
    /// Reads the list straight from the installed server's dbtool.py so it always matches that server version,
    /// falling back to <see cref="Fallback"/> if the file is missing or its format changed.
    /// </summary>
    public static IReadOnlyList<string> Load(string serverDir)
    {
        var dbtool = Path.Combine(serverDir, "tools", "dbtool.py");
        if (!File.Exists(dbtool))
        {
            return Fallback;
        }

        var block = PlayerDataBlock().Match(File.ReadAllText(dbtool));
        if (!block.Success)
        {
            return Fallback;
        }

        var tables = SqlFileName().Matches(block.Groups[1].Value).Select(m => m.Groups[1].Value).Distinct().ToList();
        return tables.Count > 10 ? tables : Fallback;
    }

    [GeneratedRegex(@"player_data\s*=\s*\[(.*?)\]", RegexOptions.Singleline)]
    private static partial Regex PlayerDataBlock();

    [GeneratedRegex(@"[""']([A-Za-z0-9_]+)\.sql[""']")]
    private static partial Regex SqlFileName();
}
