namespace LocalXI.Core.Game;

public sealed record InstallCheck(string Name, bool Passed, string Detail);

/// <summary>What we know about a player's own FINAL FANTASY XI install. We only ever read from it.</summary>
public sealed class FfxiInstallation
{
    private static readonly string[] ExpansionFolders = ["ROM", "ROM2", "ROM3", "ROM4", "ROM5", "ROM6", "ROM7", "ROM8", "ROM9"];

    private FfxiInstallation(string ffxiPath, string? playOnlineViewerPath, DateTime? clientBuildDate, IReadOnlyList<InstallCheck> checks)
    {
        FfxiPath = ffxiPath;
        PlayOnlineViewerPath = playOnlineViewerPath;
        ClientBuildDate = clientBuildDate;
        Checks = checks;
    }

    public string FfxiPath { get; }
    public string? PlayOnlineViewerPath { get; }

    /// <summary>When FFXiMain.dll was last written. Game patches replace it, so this tracks the installed client version.</summary>
    public DateTime? ClientBuildDate { get; }

    public IReadOnlyList<InstallCheck> Checks { get; }
    public bool IsValid => Checks.All(c => c.Passed);

    public static FfxiInstallation Inspect(string ffxiPath)
    {
        ffxiPath = Path.GetFullPath(ffxiPath.Trim().TrimEnd('\\', '/'));
        var checks = new List<InstallCheck>();

        var mainDll = Path.Combine(ffxiPath, "FFXiMain.dll");
        var hasMain = File.Exists(mainDll);
        checks.Add(new("Game program", hasMain, hasMain ? "FFXiMain.dll found" : "FFXiMain.dll is missing - is this the \"FINAL FANTASY XI\" folder?"));

        var hasTables = File.Exists(Path.Combine(ffxiPath, "FTABLE.DAT")) && File.Exists(Path.Combine(ffxiPath, "VTABLE.DAT"));
        checks.Add(new("Data index", hasTables, hasTables ? "FTABLE.DAT and VTABLE.DAT found" : "FTABLE.DAT / VTABLE.DAT are missing"));

        var missing = ExpansionFolders.Where(f => !Directory.Exists(Path.Combine(ffxiPath, f))).ToList();
        checks.Add(new("Game data", missing.Count == 0, missing.Count == 0 ? "All expansion data folders found" : "Missing: " + string.Join(", ", missing)));

        var viewer = FindPlayOnlineViewer(ffxiPath);
        checks.Add(new("PlayOnline", viewer is not null, viewer is not null
            ? "PlayOnline Viewer found"
            : "PlayOnline Viewer (polcore.dll) not found. It is installed alongside the game by Square Enix's installer."));

        DateTime? buildDate = hasMain ? File.GetLastWriteTime(mainDll) : null;
        return new FfxiInstallation(ffxiPath, viewer, buildDate, checks);
    }

    /// <summary>
    /// Compares the client against the server's expected version string (e.g. "30261006_0", where the date is
    /// 2026-10-06 written with a leading 3). Returns null when we can't tell.
    /// </summary>
    public bool? IsUpToDateFor(string? serverClientVersion)
    {
        if (ClientBuildDate is not { } built || ParseClientVersionDate(serverClientVersion) is not { } expected)
        {
            return null;
        }

        // Patch files can be stamped a few weeks before release; allow generous slack.
        return built >= expected.AddDays(-45);
    }

    public static DateTime? ParseClientVersionDate(string? version)
    {
        if (version is null || version.Length < 8 || version[0] != '3' || !version[..8].All(char.IsAsciiDigit))
        {
            return null;
        }

        var digits = "2" + version[1..8];
        return DateTime.TryParseExact(digits, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date) ? date : null;
    }

    public static string? FindPlayOnlineViewer(string ffxiPath)
    {
        var candidates = new List<string>();
        if (PlayOnlineRegistry.Read() is { ViewerPath: { } registered })
        {
            candidates.Add(registered);
        }

        var parent = Directory.GetParent(ffxiPath)?.FullName;
        if (parent is not null)
        {
            candidates.Add(Path.Combine(parent, "PlayOnlineViewer"));
        }

        return candidates
            .Select(c => c.TrimEnd('\\', '/'))
            .FirstOrDefault(c => File.Exists(Path.Combine(c, "viewer", "com", "polcore.dll")));
    }
}

public static class FfxiLocator
{
    /// <summary>Likely install folders, best guesses first. Includes the registry and the official/Steam default paths.</summary>
    public static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (PlayOnlineRegistry.Read() is { FfxiPath: { } registered } && seen.Add(registered.TrimEnd('\\')))
        {
            yield return registered.TrimEnd('\\');
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] relative =
        [
            @"PlayOnline\SquareEnix\FINAL FANTASY XI",
            @"SquareEnix\FINAL FANTASY XI",
            @"Steam\steamapps\common\FFXINA\SquareEnix\FINAL FANTASY XI",
            @"Steam\steamapps\common\FFXIEU\SquareEnix\FINAL FANTASY XI",
        ];

        foreach (var root in new[] { programFilesX86, programFiles })
        {
            foreach (var rel in relative)
            {
                var path = Path.Combine(root, rel);
                if (Directory.Exists(path) && seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }

    public static string? FindBest()
    {
        string? fallback = null;
        foreach (var candidate in Candidates())
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            if (FfxiInstallation.Inspect(candidate).IsValid)
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        return fallback;
    }
}
