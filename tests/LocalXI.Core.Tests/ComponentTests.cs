using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using LocalXI.Core.Components;

namespace LocalXI.Core.Tests;

public class ComponentManifestTests
{
    [Fact]
    public void EmbeddedManifest_ParsesEveryComponent()
    {
        var manifest = ComponentManifest.LoadEmbedded();

        Assert.Equal(ComponentKind.Zip, manifest["mariadb"].Kind);
        Assert.Equal(ComponentKind.ZipOverlay, manifest["meshes"].Kind);
        Assert.Equal(ComponentKind.File, manifest["xiloader"].Kind);
        Assert.Equal(ComponentRole.Client, manifest["ashita"].Role);
        Assert.Equal(ComponentRole.Server, manifest["server"].Role);
        Assert.Contains("navmeshes", manifest["server"].Preserve);
        Assert.Equal(ComponentKind.Installer, manifest["vcredist-x86"].Kind);
        Assert.Equal("vcruntime-x86:14.44", manifest["vcredist-x86"].Detect);
    }

    [Fact]
    public void JoinMode_OnlyNeedsClientComponents()
    {
        var manifest = ComponentManifest.LoadEmbedded();
        Assert.Equal(["vcredist-x86", "ashita", "xiloader"], manifest.ForRole(includeServer: false).Select(c => c.Id));
    }

    [Fact]
    public void PinnedThirdPartyComponents_HaveHashes()
    {
        var manifest = ComponentManifest.LoadEmbedded();
        foreach (var id in new[] { "vcredist-x86", "vcredist-x64", "mariadb", "ashita", "xiloader" })
        {
            Assert.Matches("^[0-9a-f]{64}$", manifest[id].Sha256);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-unknown:1.0")]
    public void UnknownPrerequisiteChecks_AreNotSatisfied(string? check) => Assert.False(Prerequisites.IsSatisfied(check));

    [Fact]
    public void Urls_ExpandRepositoryAndVersion()
    {
        var spec = ComponentManifest.LoadEmbedded()["server"];
        Assert.Equal(
            $"https://github.com/me/LocalXI/releases/download/{spec.Version}/localxi-server-win64.zip",
            spec.ResolveUrl("me/LocalXI"));
    }
}

public class ComponentInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localxi-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ZipInstall_StripsTopFolderAndPreservesFoldersOnUpgrade()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "user"));
        paths.EnsureCreated();

        var v1 = MakeZip(("pkg/xi_map.exe", "v1"), ("pkg/log/old.txt", "from package"));
        var v2 = MakeZip(("pkg/xi_map.exe", "v2"));

        // First install, then simulate runtime data that must survive an upgrade.
        await Install(paths, Spec("1", v1), v1);
        var serverDir = Path.Combine(paths.Runtime, "server");
        Assert.Equal("v1", File.ReadAllText(Path.Combine(serverDir, "xi_map.exe")));
        Directory.CreateDirectory(Path.Combine(serverDir, "navmeshes"));
        File.WriteAllText(Path.Combine(serverDir, "navmeshes", "zone.nav"), "mesh");

        var installer = await Install(paths, Spec("2", v2), v2);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(serverDir, "xi_map.exe")));
        Assert.Equal("mesh", File.ReadAllText(Path.Combine(serverDir, "navmeshes", "zone.nav")));
        Assert.True(File.Exists(Path.Combine(serverDir, "log", "old.txt")), "preserved log folder should carry across");
        Assert.True(installer.IsInstalled(Spec("2", v2)));
        Assert.False(installer.IsInstalled(Spec("1", v1)));
    }

    [Fact]
    public async Task ZipUpgrade_KeepsOnlyTheNestedPreservedFolder()
    {
        // Ashita keeps its game-memory tables in config/ashita (must update) and user settings in config/addons (must keep).
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "user"));
        paths.EnsureCreated();
        var v1 = MakeZip(("pkg/Ashita-cli.exe", "v1"), ("pkg/config/ashita/ashita.pointers.ini", "old pointers"));
        var v2 = MakeZip(("pkg/Ashita-cli.exe", "v2"), ("pkg/config/ashita/ashita.pointers.ini", "new pointers"));
        ComponentSpec Ashita(string version, byte[] zip) => new()
        {
            Id = "ashita", Name = "Ashita", Version = version, Kind = ComponentKind.Zip, Url = "https://example.invalid/a.zip",
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)), InstallDir = "ashita", StripTopFolder = true,
            Preserve = ["config/addons"], Probe = "Ashita-cli.exe",
        };

        await Install(paths, Ashita("1", v1), v1);
        var addonSettings = Path.Combine(paths.Runtime, "ashita", "config", "addons", "fps", "Seamus_1", "settings.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(addonSettings)!);
        File.WriteAllText(addonSettings, "mine");

        await Install(paths, Ashita("2", v2), v2);

        Assert.Equal("new pointers", File.ReadAllText(Path.Combine(paths.Runtime, "ashita", "config", "ashita", "ashita.pointers.ini")));
        Assert.Equal("mine", File.ReadAllText(addonSettings));
    }

    [Fact]
    public async Task Download_WithWrongHash_IsRejected()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "user"));
        var zip = MakeZip(("pkg/xi_map.exe", "v1"));
        var spec = Spec("1", zip, sha256Override: new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(paths, spec, zip));
        Assert.False(File.Exists(Path.Combine(paths.Downloads, "server-1.zip")));
    }

    [Fact]
    public async Task UnpublishedComponent_ExplainsWhatToDo()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "user"));
        var spec = new ComponentSpec { Id = "server", Name = "Server", Version = "x", Url = "https://example.invalid/", InstallDir = "server", Kind = ComponentKind.Zip };
        var installer = new ComponentInstaller(paths, new HttpClient(new StaticHandler([])), "me/LocalXI");

        var error = await Assert.ThrowsAsync<ComponentUnavailableException>(() => installer.InstallAsync(spec, null, CancellationToken.None));
        Assert.Contains("RELEASING", error.Message);
    }

    private static ComponentSpec Spec(string version, byte[] zip, string? sha256Override = null) => new()
    {
        Id = "server",
        Name = "Server",
        Version = version,
        Kind = ComponentKind.Zip,
        Url = "https://example.invalid/server.zip",
        Sha256 = sha256Override ?? Convert.ToHexStringLower(SHA256.HashData(zip)),
        InstallDir = "server",
        StripTopFolder = true,
        Preserve = ["navmeshes", "log"],
        Probe = "xi_map.exe",
    };

    private static async Task<ComponentInstaller> Install(AppPaths paths, ComponentSpec spec, byte[] zip)
    {
        var installer = new ComponentInstaller(paths, new HttpClient(new StaticHandler(zip)), "me/LocalXI");
        await installer.InstallAsync(spec, null, CancellationToken.None);
        return installer;
    }

    private static byte[] MakeZip(params (string Path, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private sealed class StaticHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
