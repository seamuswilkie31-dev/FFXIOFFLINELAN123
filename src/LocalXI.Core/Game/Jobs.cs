namespace LocalXI.Core.Game;

public static class Jobs
{
    // Indexed by the job ids LandSandBoat stores in char_stats.mjob.
    private static readonly string[] Abbreviations =
        ["", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN"];

    private static readonly string[] Names =
    [
        "", "Warrior", "Monk", "White Mage", "Black Mage", "Red Mage", "Thief", "Paladin", "Dark Knight", "Beastmaster", "Bard",
        "Ranger", "Samurai", "Ninja", "Dragoon", "Summoner", "Blue Mage", "Corsair", "Puppetmaster", "Dancer", "Scholar",
        "Geomancer", "Rune Fencer",
    ];

    public static string Abbreviation(int jobId) => jobId >= 0 && jobId < Abbreviations.Length ? Abbreviations[jobId] : "";

    public static string Name(int jobId) => jobId >= 0 && jobId < Names.Length ? Names[jobId] : "";

    /// <summary>Geomancer and Rune Fencer arrived with Seekers of Adoulin (2013), after the level-75 era.</summary>
    public static bool IsClassic(int jobId) => jobId is >= 1 and <= 20;
}
