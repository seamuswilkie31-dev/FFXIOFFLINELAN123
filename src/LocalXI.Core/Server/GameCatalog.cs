using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalXI.Core.Server;

public sealed record HomePointInfo(int Index, string Name);

public sealed record QuestInfo(int Area, int Id, string Key)
{
    public string Name => GameCatalog.Prettify(Key);
}

/// <summary>
/// Game reference data read from the installed server's own Lua scripts, so the character editor always matches
/// the server version: home point crystals, quest ids and fame rank thresholds.
/// </summary>
public sealed partial class GameCatalog
{
    /// <summary>Quest log areas in xi.questLog order (scripts/enum/quest_log.lua).</summary>
    public static readonly IReadOnlyList<string> QuestAreaNames =
        ["San d'Oria", "Bastok", "Windurst", "Jeuno", "Other Areas", "Outlands", "Aht Urhgan", "Crystal War", "Abyssea", "Adoulin", "Coalition"];

    /// <summary>xi.data.fame.rankPoints as of LandSandBoat 1f204df (fame level 1-9).</summary>
    public static readonly IReadOnlyList<int> DefaultFameRankPoints = [0, 50, 125, 225, 325, 425, 488, 550, 613];

    private static readonly Dictionary<string, int> DefaultQuestAreas = new(StringComparer.Ordinal)
    {
        ["SANDORIA"] = 0, ["BASTOK"] = 1, ["WINDURST"] = 2, ["JEUNO"] = 3, ["OTHER_AREAS"] = 4, ["OUTLANDS"] = 5,
        ["AHT_URHGAN"] = 6, ["CRYSTAL_WAR"] = 7, ["ABYSSEA"] = 8, ["ADOULIN"] = 9, ["COALITION"] = 10,
    };

    private GameCatalog(IReadOnlyList<HomePointInfo> homePoints, IReadOnlyList<QuestInfo> quests, IReadOnlyList<int> fameRankPoints)
    {
        HomePoints = homePoints;
        Quests = quests;
        FameRankPoints = fameRankPoints;
    }

    public IReadOnlyList<HomePointInfo> HomePoints { get; }
    public IReadOnlyList<QuestInfo> Quests { get; }

    /// <summary>Fame points needed for levels 1..9 (index 0 is level 1).</summary>
    public IReadOnlyList<int> FameRankPoints { get; }

    public static GameCatalog Load(string serverDir)
    {
        string? Read(params string[] parts)
        {
            var path = Path.Combine([serverDir, .. parts]);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        var areas = Read("scripts", "enum", "quest_log.lua") is { } questLog ? ParseQuestAreas(questLog) : DefaultQuestAreas;
        var fame = Read("scripts", "data", "fame.lua") is { } fameLua ? ParseFameRankPoints(fameLua) : [];

        return new GameCatalog(
            Read("scripts", "globals", "homepoint.lua") is { } homepoints ? ParseHomePoints(homepoints) : [],
            Read("scripts", "globals", "quests.lua") is { } quests ? ParseQuests(quests, areas) : [],
            fame.Count == 9 ? fame : DefaultFameRankPoints);
    }

    public int FameLevel(int points)
    {
        var level = 1;
        for (var i = 0; i < FameRankPoints.Count; i++)
        {
            if (points >= FameRankPoints[i])
            {
                level = i + 1;
            }
        }

        return level;
    }

    /// <summary>Lines like <c>[  6] = { group = 1, ... }, -- Port San d'Oria #1</c>.</summary>
    internal static IReadOnlyList<HomePointInfo> ParseHomePoints(string lua) =>
        HomePointLine().Matches(lua)
            .Select(m => new HomePointInfo(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value.Trim()))
            .DistinctBy(h => h.Index)
            .OrderBy(h => h.Index)
            .ToList();

    /// <summary>
    /// The xi.quest.id table: <c>[xi.quest.area[xi.questLog.SANDORIA]] =</c> headers, each followed by
    /// <c>QUEST_NAME = id,</c> lines.
    /// </summary>
    internal static IReadOnlyList<QuestInfo> ParseQuests(string lua, IReadOnlyDictionary<string, int> areas)
    {
        var start = lua.IndexOf("xi.quest.id", StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        var quests = new List<QuestInfo>();
        int? area = null;
        foreach (var line in lua[start..].Split('\n'))
        {
            var header = QuestAreaHeader().Match(line);
            if (header.Success)
            {
                area = areas.TryGetValue(header.Groups[1].Value, out var id) ? id : null;
                continue;
            }

            var entry = QuestEntry().Match(line);
            if (area is { } current && entry.Success)
            {
                quests.Add(new QuestInfo(current, int.Parse(entry.Groups[2].Value, CultureInfo.InvariantCulture), entry.Groups[1].Value));
            }
        }

        return quests;
    }

    internal static IReadOnlyDictionary<string, int> ParseQuestAreas(string lua)
    {
        var parsed = EnumEntry().Matches(lua)
            .Select(m => (Name: m.Groups[1].Value, Value: int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Where(e => e.Value >= 0)
            .ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
        return parsed.Count > 0 ? parsed : DefaultQuestAreas;
    }

    internal static IReadOnlyList<int> ParseFameRankPoints(string lua)
    {
        var table = FameTable().Match(lua);
        return table.Success
            ? FameEntry().Matches(table.Groups[1].Value)
                .OrderBy(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .Select(m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
                .ToList()
            : [];
    }

    /// <summary>A_SENTRYS_PERIL becomes "A Sentrys Peril"; roman numerals such as II stay upper case.</summary>
    public static string Prettify(string key) =>
        string.Join(' ', key.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word =>
            RomanNumeral().IsMatch(word) ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    [GeneratedRegex(@"^\s*\[\s*(\d+)\s*\]\s*=\s*\{.*\}\s*,\s*--\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex HomePointLine();

    [GeneratedRegex(@"\[xi\.quest\.area\[xi\.questLog\.(\w+)\]\]\s*=")]
    private static partial Regex QuestAreaHeader();

    [GeneratedRegex(@"^\s*([A-Z0-9_]+)\s*=\s*(\d+)\s*,")]
    private static partial Regex QuestEntry();

    [GeneratedRegex(@"^\s*([A-Z_]+)\s*=\s*(-?\d+)\s*,", RegexOptions.Multiline)]
    private static partial Regex EnumEntry();

    [GeneratedRegex(@"rankPoints\s*=\s*\{(.*?)\}", RegexOptions.Singleline)]
    private static partial Regex FameTable();

    [GeneratedRegex(@"\[\s*(\d+)\s*\]\s*=\s*(\d+)")]
    private static partial Regex FameEntry();

    [GeneratedRegex("^[IVX]+$")]
    private static partial Regex RomanNumeral();
}
