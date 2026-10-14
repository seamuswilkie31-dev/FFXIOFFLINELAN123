using System.Text.Json;
using System.Text.Json.Serialization;
using LocalXI.Core.Util;

namespace LocalXI.Core.Components;

public enum ComponentKind
{
    /// <summary>A zip that fully replaces its install folder (keeping any <see cref="ComponentSpec.Preserve"/> subfolders).</summary>
    Zip,

    /// <summary>A zip extracted on top of another component's folder (e.g. meshes into the server folder).</summary>
    ZipOverlay,

    /// <summary>A single file copied into place.</summary>
    File,

    /// <summary>A Windows installer run once (e.g. the Visual C++ runtime); <see cref="ComponentSpec.Detect"/> says whether it's needed.</summary>
    Installer,
}

public enum ComponentRole
{
    /// <summary>Needed only by a PC that runs the server (Solo and Host modes).</summary>
    Server,

    /// <summary>Needed by every PC that plays the game.</summary>
    Client,
}

public sealed class ComponentSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ComponentRole Role { get; init; }
    public required string Version { get; init; }

    [JsonConverter(typeof(KebabCaseEnumConverter<ComponentKind>))]
    public ComponentKind Kind { get; init; }

    public required string Url { get; init; }
    public string? Sha256 { get; init; }

    /// <summary>Folder relative to the launcher's runtime directory.</summary>
    public required string InstallDir { get; init; }

    public string? FileName { get; init; }
    public bool StripTopFolder { get; init; }
    public IReadOnlyList<string> Preserve { get; init; } = [];

    /// <summary>A file or folder (relative to InstallDir) whose absence means the component needs reinstalling.</summary>
    public string? Probe { get; init; }

    /// <summary>Command-line arguments for <see cref="ComponentKind.Installer"/> components.</summary>
    public string? InstallArguments { get; init; }

    /// <summary>For installers: a system check such as "vcruntime-x86:14.44" (see <see cref="Prerequisites"/>).</summary>
    public string? Detect { get; init; }

    public string? License { get; init; }
    public string? Homepage { get; init; }

    /// <summary>The server package and meshes are built by this repo's CI; until a release exists there is no hash to pin.</summary>
    public bool IsPublished => !string.IsNullOrWhiteSpace(Sha256);

    public string ResolveUrl(string repository) => Url.Replace("{repository}", repository).Replace("{version}", Version);
}

public sealed class ComponentManifest
{
    public int FormatVersion { get; init; }
    public required string Repository { get; init; }
    public IReadOnlyList<ComponentSpec> Components { get; init; } = [];

    public ComponentSpec this[string id] => Components.First(c => c.Id == id);

    public IEnumerable<ComponentSpec> ForRole(bool includeServer) =>
        Components.Where(c => includeServer || c.Role == ComponentRole.Client);

    public static ComponentManifest LoadEmbedded()
    {
        using var stream = typeof(ComponentManifest).Assembly.GetManifestResourceStream("components.json")
            ?? throw new InvalidOperationException("components.json is missing from the build.");
        return Parse(stream);
    }

    public static ComponentManifest Parse(Stream json) =>
        JsonSerializer.Deserialize<ComponentManifest>(json, JsonDefaults.Options)
        ?? throw new InvalidDataException("components.json is empty.");
}

/// <summary>Reads "zip-overlay" as <see cref="ComponentKind.ZipOverlay"/>.</summary>
internal sealed class KebabCaseEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.KebabCaseLower)
    where T : struct, Enum;
