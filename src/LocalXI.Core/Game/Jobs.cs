namespace LocalXI.Core.Game;

public static class Jobs
{
    // Indexed by the job ids LandSandBoat stores in char_stats.mjob.
    private static readonly string[] Abbreviations =
        ["", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN"];

    public static string Abbreviation(int jobId) => jobId >= 0 && jobId < Abbreviations.Length ? Abbreviations[jobId] : "";
}
