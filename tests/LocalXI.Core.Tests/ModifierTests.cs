using LocalXI.Core.Database;
using LocalXI.Core.Server;

namespace LocalXI.Core.Tests;

public class QuestLogBlobTests
{
    [Fact]
    public void EmptyLog_GrowsToElevenAreasOf64Bytes()
    {
        var blob = QuestLogBlob.Set([], area: 0, quest: 1, QuestState.Accepted);
        Assert.Equal(11 * 64, blob.Length);
    }

    [Fact]
    public void BitsLandWhereCharutilsReadsThem()
    {
        // charutils.cpp: m_questLog[log].current[quest / 8] & (1 << (quest % 8)); complete follows the 32 current bytes.
        var blob = QuestLogBlob.Set(null, area: 2, quest: 13, QuestState.Accepted);
        Assert.Equal(1 << 5, blob[2 * 64 + 13 / 8]);
        Assert.Equal(0, blob[2 * 64 + 32 + 13 / 8]);

        blob = QuestLogBlob.Set(blob, area: 2, quest: 13, QuestState.Completed);
        Assert.Equal(0, blob[2 * 64 + 13 / 8]);
        Assert.Equal(1 << 5, blob[2 * 64 + 32 + 13 / 8]);
    }

    [Theory]
    [InlineData(QuestState.NotStarted)]
    [InlineData(QuestState.Accepted)]
    [InlineData(QuestState.Completed)]
    public void RoundTripsEveryState(QuestState state)
    {
        var blob = QuestLogBlob.Set(null, 10, 255, QuestState.Completed);
        blob = QuestLogBlob.Set(blob, 10, 255, state);
        Assert.Equal(state, QuestLogBlob.Get(blob, 10, 255));
    }

    [Fact]
    public void LeavesOtherQuestsAlone()
    {
        var blob = QuestLogBlob.Set(null, 0, 0, QuestState.Completed);
        blob = QuestLogBlob.Set(blob, 0, 1, QuestState.Accepted);
        blob = QuestLogBlob.Set(blob, 0, 1, QuestState.NotStarted);
        Assert.Equal(QuestState.Completed, QuestLogBlob.Get(blob, 0, 0));
    }

    [Fact]
    public void RejectsOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestLogBlob.Set(null, 11, 0, QuestState.Accepted));
}

public class HomePointBlobTests
{
    [Fact]
    public void RegistersBitsLikeHomepointLua()
    {
        // homepoint.lua: hpBit = index % 32, hpSet = floor(index / 32) -> access[hpSet] |= 1 << hpBit (little-endian).
        var blob = HomePointBlob.Set(null, [0, 33, 121], registered: true);
        Assert.Equal(HomePointBlob.Size, blob.Length);
        Assert.Equal(1, blob[0]);
        Assert.Equal(1 << 1, blob[4]);           // index 33 = access[1] bit 1
        Assert.Equal(1 << 1, blob[121 / 8]);     // index 121 = access[3] bit 25 = byte 15 bit 1
        Assert.True(HomePointBlob.IsRegistered(blob, 33));
        Assert.False(HomePointBlob.IsRegistered(blob, 34));
    }

    [Fact]
    public void KeepsFavouritesAfterTheAccessWords()
    {
        var existing = new byte[HomePointBlob.Size];
        existing[20] = 7; // inside int32 menu[10]
        var blob = HomePointBlob.Set(existing, Enumerable.Range(0, 122), registered: true);
        Assert.Equal(7, blob[20]);
        Assert.True(Enumerable.Range(0, 122).All(i => HomePointBlob.IsRegistered(blob, i)));

        blob = HomePointBlob.Set(blob, Enumerable.Range(0, 122), registered: false);
        Assert.False(HomePointBlob.IsRegistered(blob, 5));
        Assert.Equal(7, blob[20]);
    }
}

public class GameCatalogTests
{
    [Fact]
    public void ParsesHomePoints()
    {
        const string lua = """
            local homepoints = {
            [  0] = { group = 1, fee = 1, dest = {  -85.554,       1, -64.554,  45, xi.zone.SOUTHERN_SAN_DORIA     } }, -- Southern San d'Oria #1
            [ 33] = { group = 2, fee = 1, dest = {  1, 2, 3, 0, xi.zone.PORT_BASTOK } }, -- Port Bastok #1
            }
            """;
        var points = GameCatalog.ParseHomePoints(lua);
        Assert.Equal([0, 33], points.Select(p => p.Index));
        Assert.Equal("Southern San d'Oria #1", points[0].Name);
    }

    [Fact]
    public void ParsesQuestTableByArea()
    {
        const string lua = """
            xi.quest.area =
            {
                [xi.questLog.SANDORIA]    = 'sandoria',
            }

            xi.quest.id =
            {
                [xi.quest.area[xi.questLog.SANDORIA]] =
                {
                    A_SENTRYS_PERIL                 = 0,  -- + Converted
                    WATERS_OF_THE_CHEVAL            = 1,
                },
                [xi.quest.area[xi.questLog.BASTOK]] =
                {
                    THE_SIREN_S_TEAR_II             = 5,
                },
            }
            """;
        var areas = new Dictionary<string, int> { ["SANDORIA"] = 0, ["BASTOK"] = 1 };
        var quests = GameCatalog.ParseQuests(lua, areas);

        Assert.Equal(3, quests.Count);
        Assert.Equal((0, 1), (quests[1].Area, quests[1].Id));
        Assert.Equal((1, 5), (quests[2].Area, quests[2].Id));
        Assert.Equal("A Sentrys Peril", quests[0].Name);
        Assert.Equal("The Siren S Tear II", quests[2].Name);
    }

    [Fact]
    public void ParsesQuestAreasAndFameThresholds()
    {
        var areas = GameCatalog.ParseQuestAreas("xi.questLog =\n{\n    NONE        = -1,\n    SANDORIA    =  0,\n    COALITION   = 10,\n}");
        Assert.False(areas.ContainsKey("NONE"));
        Assert.Equal(10, areas["COALITION"]);

        var fame = GameCatalog.ParseFameRankPoints("xi.data.fame.rankPoints =\n{\n    [1] = 0,\n    [2] = 50,\n    [9] = 613,\n}");
        Assert.Equal([0, 50, 613], fame);
    }

    [Fact]
    public void MissingScriptsFallBackToDefaults()
    {
        var catalog = GameCatalog.Load(Path.Combine(Path.GetTempPath(), "no-such-server-" + Guid.NewGuid()));
        Assert.Empty(catalog.HomePoints);
        Assert.Equal(GameCatalog.DefaultFameRankPoints, catalog.FameRankPoints);
        Assert.Equal(9, catalog.FameLevel(613));
        Assert.Equal(2, catalog.FameLevel(124));
        Assert.Equal(1, catalog.FameLevel(0));
    }
}

public class ServerSettingsFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localxi-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MapLua => Path.Combine(_root, "settings", "map.lua");

    private void WriteDefaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, "settings", "default"));
        File.WriteAllText(Path.Combine(_root, "settings", "default", "map.lua"),
            "xi.settings.map =\n{\n    EXP_RATE                = 1.0,\n    EXP_LOSS_RATE           = 1.0,\n}\n");
    }

    [Fact]
    public void WritesACopyWithOnlyExpRateChanged()
    {
        WriteDefaults();
        Assert.True(ServerSettingsFiles.ApplyExpRate(_root, 2.5));

        var text = File.ReadAllText(MapLua);
        Assert.Contains("EXP_RATE                = 2.5,", text);
        Assert.Contains("EXP_LOSS_RATE           = 1.0,", text);
    }

    [Fact]
    public void BackToOneRemovesTheCopy()
    {
        WriteDefaults();
        ServerSettingsFiles.ApplyExpRate(_root, 3.0);
        Assert.True(ServerSettingsFiles.ApplyExpRate(_root, 1.0));
        Assert.False(File.Exists(MapLua));
    }

    [Fact]
    public void LeavesAHandWrittenFileAlone()
    {
        WriteDefaults();
        File.WriteAllText(MapLua, "-- my own settings\n");
        Assert.False(ServerSettingsFiles.ApplyExpRate(_root, 2.0));
        Assert.Equal("-- my own settings\n", File.ReadAllText(MapLua));
    }
}
