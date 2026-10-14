using System.Text.Json;
using System.Text.RegularExpressions;
using LocalXI.Core.Database;
using LocalXI.Core.Util;

namespace LocalXI.Core.Server;

/// <summary>Facts about an installed LandSandBoat build.</summary>
public sealed partial class ServerPackage
{
    /// <summary>Written by this repo's build-server workflow into the root of every server package.</summary>
    public const string ManifestFileName = "localxi-server.json";

    private sealed record Manifest(string? LsbCommit, string? LsbRef, string? ClientVersion, string? BuiltAt);

    private ServerPackage(string directory, string? lsbCommit, string? clientVersion, IReadOnlyList<string> playerTables)
    {
        Directory = directory;
        LsbCommit = lsbCommit;
        ClientVersion = clientVersion;
        PlayerTables = playerTables;
    }

    public string Directory { get; }
    public string? LsbCommit { get; }

    /// <summary>The game client version the server expects, e.g. "30261006_0".</summary>
    public string? ClientVersion { get; }

    public IReadOnlyList<string> PlayerTables { get; }

    public bool LooksValid =>
        File.Exists(Executable("xi_map")) &&
        File.Exists(Executable("xi_connect")) &&
        System.IO.Directory.Exists(Path.Combine(Directory, "sql")) &&
        System.IO.Directory.Exists(Path.Combine(Directory, "scripts"));

    public string Executable(string name) => Path.Combine(Directory, name + ".exe");

    public static ServerPackage Load(string directory)
    {
        string? commit = null;
        string? clientVersion = null;

        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonDefaults.Options);
                commit = manifest?.LsbCommit;
                clientVersion = manifest?.ClientVersion;
            }
            catch (JsonException)
            {
                // Fall through to reading the server's own files.
            }
        }

        clientVersion ??= ReadClientVersion(Path.Combine(directory, "settings", "default", "login.lua"));
        return new ServerPackage(directory, commit, clientVersion, Database.PlayerTables.Load(directory));
    }

    private static string? ReadClientVersion(string loginLua)
    {
        if (!File.Exists(loginLua))
        {
            return null;
        }

        var match = ClientVersionSetting().Match(File.ReadAllText(loginLua));
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"CLIENT_VER\s*=\s*['""]([^'""]+)['""]")]
    private static partial Regex ClientVersionSetting();
}
