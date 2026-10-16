using LocalXI.Core.Database;

namespace LocalXI.Core.Server;

/// <summary>
/// LocalXI's own LandSandBoat module (modules/localxi), rewritten each time the downloaded server starts. It carries
/// the per-character rules from the Modifiers page that the database alone can't express.
/// </summary>
public static class LocalXiModule
{
    public const string Folder = "localxi";

    /// <summary>Character variable that gives one character Dual Wield on every job.</summary>
    public const string DualWieldVar = "[LocalXI]DualWield";

    // xi.trait.DUAL_WIELD, xi.mod.DUAL_WIELD, and Dual Wield I's 10% delay reduction (traits.sql).
    private const int DualWieldTrait = 18;
    private const int DualWieldMod = 259;
    private const int DualWieldValue = 10;

    // Marks LocalXI's rows: the game's own Dual Wield traits never start at level 1.
    private const int DualWieldLevel = 1;

    // xi.job WAR (1) to RUN (22).
    private static readonly IEnumerable<int> Jobs = Enumerable.Range(1, 22);

    /// <summary>
    /// The server only lets a character hold an off-hand weapon with the Dual Wield trait, and rebuilds traits from
    /// job and level on every login, level and job change; scripts can't add one. They can veto one, though
    /// (xi.traits.canApplyTrait), so every job gets a level-1 Dual Wield trait and this module lets it through only
    /// for player characters whose variable is set. Monsters, pets and trusts never get it.
    /// </summary>
    private static readonly string DualWieldModule = $$"""
        -----------------------------------
        -- Written by LocalXI each time the server starts; changes here are overwritten.
        -- Per-character Dual Wield (Modifiers page > Abilities). LocalXI adds a level-1 Dual Wield trait to every
        -- job in the traits table; this lets it apply only to characters whose '{{DualWieldVar}}' variable is 1.
        -----------------------------------
        require('modules/module_utils')
        -----------------------------------

        local m = Module:new('localxi_dual_wield')

        m:addOverride('xi.traits.canApplyTrait', function(trait, entity, zone)
            if trait:getID() == xi.trait.DUAL_WIELD and trait:getLevel() == {{DualWieldLevel}} then
                return entity:isPC() and entity:getCharVar('{{DualWieldVar}}') == 1
            end

            return super(trait, entity, zone)
        end)

        """;

    public static void Write(string serverDir)
    {
        var folder = Path.Combine(serverDir, "modules", Folder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dual_wield.lua"), DualWieldModule.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Adds the level-1 Dual Wield trait rows the module filters. The traits table is game data, so a data reload
    /// (server update, era switch) drops them; they're added again before every start.
    /// </summary>
    public static Task AddDualWieldTraitsAsync(GameDatabase database, CancellationToken cancellationToken = default) =>
        database.ExecuteAsync(
            "INSERT IGNORE INTO traits (traitid, name, job, level, `rank`, modifier, value, content_tag, meritid) VALUES " +
            string.Join(", ", Jobs.Select(job => $"({DualWieldTrait}, 'dual wield', {job}, {DualWieldLevel}, 1, {DualWieldMod}, {DualWieldValue}, NULL, 0)")),
            cancellationToken: cancellationToken);
}
