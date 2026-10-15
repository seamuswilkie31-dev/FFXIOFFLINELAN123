namespace LocalXI.Core.Config;

public enum PlayMode
{
    /// <summary>Server and game on this PC, reachable only from this PC.</summary>
    Solo,

    /// <summary>Server on this PC, reachable by friends over LAN or a VPN such as Radmin.</summary>
    Host,

    /// <summary>No local server; connect to a friend who is hosting.</summary>
    Join,
}

public enum GameEra
{
    /// <summary>The level-75 era up to Wings of the Goddess, like classic-era community servers.</summary>
    Classic75,

    /// <summary>Current retail rules: level 99 and every expansion.</summary>
    Modern,
}

/// <summary>Values match the game's own registry setting 0034 (see Ashita docs for [ffxi.registry]).</summary>
public enum WindowMode
{
    Fullscreen = 0,
    Windowed = 1,
    BorderlessWindowed = 3,
}

public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool SetupComplete { get; set; }
    public PlayMode Mode { get; set; } = PlayMode.Solo;
    public string? FfxiPath { get; set; }
    public DatabaseSettings Database { get; set; } = new();
    public AccountSettings Account { get; set; } = new();
    public HostSettings Host { get; set; } = new();
    public JoinSettings Join { get; set; } = new();
    public GameWindowSettings Window { get; set; } = new();
    public ServerOptions Server { get; set; } = new();
    public BackupSettings Backups { get; set; } = new();

    public bool RunsLocalServer => Mode != PlayMode.Join;
}

public sealed class DatabaseSettings
{
    public const int DefaultPort = 3307;

    public int Port { get; set; } = DefaultPort;
    public string RootPassword { get; set; } = "";
    public string Name { get; set; } = "xidb";
}

/// <summary>The game account the launcher logs in with (an account on the LandSandBoat server, not Square Enix).</summary>
public sealed class AccountSettings
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class HostSettings
{
    /// <summary>The address friends connect to, e.g. a Radmin VPN 26.x.x.x address or a LAN 192.168.x.x address.</summary>
    public string AdvertisedIp { get; set; } = "";
}

public sealed class JoinSettings
{
    public string ServerAddress { get; set; } = "";
}

public sealed class GameWindowSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public WindowMode Mode { get; set; } = WindowMode.Windowed;
}

public sealed class ServerOptions
{
    public GameEra Era { get; set; } = GameEra.Classic75;

    /// <summary>Load zones on demand: much faster start-up and less memory for small groups.</summary>
    public bool LazyZones { get; set; } = true;

    /// <summary>Let clients older than the server's expected version log in (they may see glitches).</summary>
    public bool AllowOlderClients { get; set; }

    public bool StopWhenGameCloses { get; set; } = true;

    /// <summary>Developer option: run a LandSandBoat build from this folder instead of the downloaded package.</summary>
    public string? OverrideDirectory { get; set; }
}

public sealed class BackupSettings
{
    public bool OnGameExit { get; set; } = true;
    public int Keep { get; set; } = 10;
}
