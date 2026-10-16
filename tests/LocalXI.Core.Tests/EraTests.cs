using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Server;

namespace LocalXI.Core.Tests;

public class EraPresetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localxi-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Classic_CapsAt75AndStopsAfterWingsOfTheGoddess()
    {
        var settings = EraPresets.Settings(GameEra.Classic75);

        Assert.Equal("75", settings["XI_MAIN_MAX_LEVEL"]);
        Assert.Equal("1", settings["XI_MAIN_RESTRICT_CONTENT"]);
        foreach (var on in new[] { "ROTZ", "COP", "TOAU", "WOTG" })
        {
            Assert.Equal("1", settings[$"XI_MAIN_ENABLE_{on}"]);
        }

        foreach (var off in new[] { "ABYSSEA", "SOA", "ROV", "TVR", "ROE" })
        {
            Assert.Equal("0", settings[$"XI_MAIN_ENABLE_{off}"]);
        }
    }

    [Fact]
    public void Classic_SettingsAreAllNumbers()
    {
        // LandSandBoat only types environment overrides correctly when they're numeric.
        Assert.All(EraPresets.Settings(GameEra.Classic75).Values, v => Assert.True(v.All(char.IsAsciiDigit)));
    }

    [Fact]
    public void Classic_LoadsEveryZoneSoEraOverridesApply()
    {
        Assert.False(EraPresets.AllowsLazyZones(GameEra.Classic75));
        Assert.True(EraPresets.AllowsLazyZones(GameEra.Modern));
    }

    [Fact]
    public void Modern_LeavesLandSandBoatDefaultsAlone()
    {
        Assert.Empty(EraPresets.Settings(GameEra.Modern));
        Assert.Empty(EraPresets.SqlPacks(GameEra.Modern));
    }

    [Fact]
    public void Classic_AppliesOnlyPacksForLaterExpansions()
    {
        var packs = EraPresets.SqlPacks(GameEra.Classic75);
        Assert.Contains("abyssea", packs);
        Assert.Contains("soa", packs);
        Assert.DoesNotContain("wotg", packs);
        Assert.DoesNotContain("toau", packs);
    }

    [Fact]
    public void EraSqlFiles_SkipsMissingPacksAndSortsWithinEach()
    {
        foreach (var (pack, file) in new[] { ("soa", "b.sql"), ("soa", "a.sql"), ("abyssea", "z.sql"), ("wotg", "w.sql") })
        {
            var dir = Directory.CreateDirectory(Path.Combine(_root, "modules", "era", "sql", pack));
            File.WriteAllText(Path.Combine(dir.FullName, file), "");
        }

        var importer = new SchemaImporter(null!, "mariadb.exe", _root);
        var files = importer.EraSqlFiles(EraPresets.SqlPacks(GameEra.Classic75)).Select(f => Path.GetRelativePath(_root, f)).ToList();

        Assert.Equal(
        [
            Path.Combine("modules", "era", "sql", "abyssea", "z.sql"),
            Path.Combine("modules", "era", "sql", "soa", "a.sql"),
            Path.Combine("modules", "era", "sql", "soa", "b.sql"),
        ], files);
    }

    [Fact]
    public void ModuleList_EnablesEraLuaOnlyWhenTheServerHasIt()
    {
        EraPresets.WriteModuleList(_root);
        Assert.False(File.Exists(Path.Combine(_root, EraPresets.ModuleListFile)));

        Directory.CreateDirectory(Path.Combine(_root, "modules", "era", "lua"));
        EraPresets.WriteModuleList(_root);
        Assert.Contains("era/lua", File.ReadAllLines(Path.Combine(_root, EraPresets.ModuleListFile)));
        Assert.DoesNotContain(LocalXiModule.Folder, File.ReadAllLines(Path.Combine(_root, EraPresets.ModuleListFile)));
    }

    [Fact]
    public void ModuleList_LoadsLocalXiModule()
    {
        Directory.CreateDirectory(Path.Combine(_root, "modules", "era", "lua"));
        LocalXiModule.Write(_root);
        EraPresets.WriteModuleList(_root);

        Assert.Equal(["era/lua", LocalXiModule.Folder], File.ReadAllLines(Path.Combine(_root, EraPresets.ModuleListFile)).Where(l => l.Length > 0 && l[0] != '#'));
        var script = File.ReadAllText(Path.Combine(_root, "modules", LocalXiModule.Folder, "dual_wield.lua"));
        Assert.Contains("m:addOverride('xi.traits.canApplyTrait'", script);
        Assert.Contains($"getCharVar('{LocalXiModule.DualWieldVar}') == 1", script);
    }
}
