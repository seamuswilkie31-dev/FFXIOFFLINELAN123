using LocalXI.Core;
using LocalXI.Core.Config;
using LocalXI.Core.Game;
using LocalXI.Core.Util;

namespace LocalXI.Core.Tests;

public class IniFileTests
{
    private static readonly string[] Sample =
    [
        "; comment",
        "[ashita.boot]",
        "file        = .\\\\bootloader\\\\pol.exe",
        "command     = --server example.com",
        "",
        "[ffxi.registry]",
        "0001 = 1920",
    ];

    [Fact]
    public void Set_ReplacesExistingKeyAndKeepsComments()
    {
        var ini = new IniFile(Sample);
        ini.Set("ashita.boot", "command", "--server 127.0.0.1");

        Assert.Equal("--server 127.0.0.1", ini.Get("ashita.boot", "command"));
        Assert.StartsWith("; comment", ini.ToString());
    }

    [Fact]
    public void Set_AddsMissingKeyInsideItsSection()
    {
        var ini = new IniFile(Sample);
        ini.Set("ashita.boot", "script", "default.txt");

        var lines = ini.ToString().Split(Environment.NewLine);
        Assert.True(Array.IndexOf(lines, "script = default.txt") < Array.IndexOf(lines, "[ffxi.registry]"));
    }

    [Fact]
    public void Set_AddsMissingSection()
    {
        var ini = new IniFile(Sample);
        ini.Set("mysqld", "bind-address", "127.0.0.1");

        Assert.Equal("127.0.0.1", ini.Get("mysqld", "bind-address"));
    }

    [Fact]
    public void Get_IsCaseInsensitiveAndScopedToSection()
    {
        var ini = new IniFile(Sample);

        Assert.Equal("1920", ini.Get("FFXI.REGISTRY", "0001"));
        Assert.Null(ini.Get("ashita.boot", "0001"));
    }
}

public class SecretsTests
{
    [Fact]
    public void NewPassword_StartsWithLetterSoLandSandBoatKeepsItAString()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = Secrets.NewPassword();
            Assert.Equal(24, password.Length);
            Assert.True(char.IsAsciiLetter(password[0]));
        }
    }

    [Theory]
    [InlineData("seamus", true)]
    [InlineData("Player_01", true)]
    [InlineData("ab", false)]
    [InlineData("has space", false)]
    [InlineData("seventeen_chars_x", false)]
    public void IsValidUsername(string username, bool expected) => Assert.Equal(expected, Secrets.IsValidUsername(username));

    [Theory]
    [InlineData("hunter22", true)]
    [InlineData("P@ss-word.2!", true)]
    [InlineData("short", false)]
    [InlineData("has space", false)]
    [InlineData("quote\"inside", false)]
    [InlineData("semi;colon", false)]
    public void IsValidPassword(string password, bool expected) => Assert.Equal(expected, Secrets.IsValidPassword(password));
}

public class ClientVersionTests
{
    [Theory]
    [InlineData("30261006_0", 2026, 10, 6)]
    [InlineData("30181205_1", 2018, 12, 5)]
    public void ParsesLandSandBoatClientVersion(string version, int year, int month, int day) =>
        Assert.Equal(new DateTime(year, month, day), FfxiInstallation.ParseClientVersionDate(version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("20261006_0")]
    [InlineData("3026xx06_0")]
    public void RejectsUnknownFormats(string? version) => Assert.Null(FfxiInstallation.ParseClientVersionDate(version));
}

public class GameLauncherTests
{
    [Fact]
    public void XiloaderCommand_PointsAtServerWithCredentials()
    {
        var command = GameLauncher.XiloaderCommand("26.10.20.30", new AccountSettings { Username = "seamus", Password = "hunter22" });
        Assert.Equal("--server 26.10.20.30 --user seamus --pass hunter22 --hide", command);
    }

    [Fact]
    public void BootConfig_UsesXiloaderAndKeepsTemplateDefaults()
    {
        var root = Directory.CreateTempSubdirectory("localxi-test-").FullName;
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, "user"));
            var bootDir = Path.Combine(paths.AshitaDir, "config", "boot");
            Directory.CreateDirectory(bootDir);
            File.WriteAllLines(Path.Combine(bootDir, "example-privateserver.ini"),
            [
                "[ashita.boot]", "file = .\\\\bootloader\\\\pol.exe", "command = --server homepointxi.com",
                "[ashita.fonts]", "d3d8.family = Arial",
            ]);

            var launcher = new GameLauncher(paths);
            launcher.WriteBootConfig("127.0.0.1", new AccountSettings { Username = "u_name", Password = "secret12" },
                new GameWindowSettings { Width = 2560, Height = 1440, Mode = WindowMode.BorderlessWindowed });

            var ini = IniFile.Load(launcher.BootConfigPath);
            Assert.Equal(@".\\bootloader\\xiloader.exe", ini.Get("ashita.boot", "file"));
            Assert.Contains("--server 127.0.0.1", ini.Get("ashita.boot", "command"));
            Assert.Equal("Arial", ini.Get("ashita.fonts", "d3d8.family"));
            Assert.Equal("2560", ini.Get("ffxi.registry", "0001"));
            Assert.Equal("3", ini.Get("ffxi.registry", "0034"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class ElevationTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files (x86)\PlayOnline", "\"C:\\Program Files (x86)\\PlayOnline\"")]
    [InlineData(@"C:\path with space\", "\"C:\\path with space\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuoteArgument_FollowsWindowsRules(string input, string expected) => Assert.Equal(expected, Elevation.QuoteArgument(input));
}
