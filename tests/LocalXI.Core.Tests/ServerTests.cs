using System.Net;
using System.Text;
using LocalXI.Core.Database;
using LocalXI.Core.Network;
using LocalXI.Core.Server;

namespace LocalXI.Core.Tests;

public class PlayerTablesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localxi-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReadsListFromDbtool()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tools"));
        var tables = string.Join(",\n", Enumerable.Range(0, 12).Select(i => $"    \"table_{i}.sql\""));
        File.WriteAllText(Path.Combine(_root, "tools", "dbtool.py"), $"# These are the 'protected' files\nplayer_data = [\n{tables},\n    \"chars.sql\",\n]\n\nimport_files = []\n");

        var loaded = PlayerTables.Load(_root);

        Assert.Equal(13, loaded.Count);
        Assert.Contains("chars", loaded);
    }

    [Fact]
    public void FallsBackWhenDbtoolMissing() => Assert.Same(PlayerTables.Fallback, PlayerTables.Load(_root));

    [Fact]
    public void FallbackCoversStoryProgress()
    {
        // Missions, quests and key items live in chars; much quest state lives in char_vars.
        Assert.Contains("chars", PlayerTables.Fallback);
        Assert.Contains("char_vars", PlayerTables.Fallback);
        Assert.Contains("char_inventory", PlayerTables.Fallback);
        Assert.Contains("accounts", PlayerTables.Fallback);
    }
}

public class ServerPackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("localxi-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReadsClientVersionFromLoginSettings()
    {
        Directory.CreateDirectory(Path.Combine(_root, "settings", "default"));
        File.WriteAllText(Path.Combine(_root, "settings", "default", "login.lua"), "xi.settings.login =\n{\n    CLIENT_VER = '30261006_0',\n}");

        Assert.Equal("30261006_0", ServerPackage.Load(_root).ClientVersion);
    }

    [Fact]
    public void PrefersLauncherManifest()
    {
        File.WriteAllText(Path.Combine(_root, ServerPackage.ManifestFileName), """{ "lsbCommit": "abc123", "clientVersion": "30250101_0" }""");

        var package = ServerPackage.Load(_root);
        Assert.Equal("abc123", package.LsbCommit);
        Assert.Equal("30250101_0", package.ClientVersion);
        Assert.False(package.LooksValid);
    }
}

public class LoginClientTests
{
    [Fact]
    public void ParsesResultCode()
    {
        var reply = LoginClient.ParseReply(Encoding.UTF8.GetBytes("{\"result\":3}\0\0\0"));
        Assert.Equal(LoginResult.SuccessCreate, reply.Result);
    }

    [Fact]
    public void ParsesErrorMessage()
    {
        var reply = LoginClient.ParseReply(Encoding.UTF8.GetBytes("{\"error_message\":\"Unsupported xiloader version 1.0.0.\"}"));
        Assert.Null(reply.Result);
        Assert.Contains("Unsupported", reply.Describe());
    }

    [Fact]
    public void ToleratesGarbage() => Assert.Null(LoginClient.ParseReply([0x02]).Result);
}

public class NetworkInfoTests
{
    [Theory]
    [InlineData("Radmin VPN", "26.12.34.56", NetworkKind.RadminVpn)]
    [InlineData("Hamachi LogMeIn Hamachi Virtual Ethernet Adapter", "25.1.2.3", NetworkKind.Hamachi)]
    [InlineData("Tailscale Tailscale Tunnel", "100.101.102.103", NetworkKind.Tailscale)]
    [InlineData("Wi-Fi Intel(R) Wi-Fi 6 AX201", "192.168.1.20", NetworkKind.Lan)]
    [InlineData("vEthernet (WSL) Hyper-V Virtual Ethernet Adapter", "172.20.0.1", NetworkKind.Virtual)]
    public void ClassifiesAdapters(string adapter, string ip, NetworkKind expected) =>
        Assert.Equal(expected, NetworkInfo.Classify(adapter, IPAddress.Parse(ip)));
}
