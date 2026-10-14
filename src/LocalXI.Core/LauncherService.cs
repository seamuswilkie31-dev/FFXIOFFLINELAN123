using System.Net.Http.Headers;
using LocalXI.Core.Components;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Logging;
using LocalXI.Core.Network;
using LocalXI.Core.Saves;
using LocalXI.Core.Server;
using LocalXI.Core.Util;

namespace LocalXI.Core;

/// <summary>Everything the UI can ask the launcher to do, in one place.</summary>
public sealed class LauncherService : IAsyncDisposable
{
    private const string MetaTable = "localxi_meta";

    private LsbServer? _server;
    private ServerPackage? _package;

    public LauncherService(AppPaths paths)
    {
        Paths = paths;
        Paths.EnsureCreated();
        Config = ConfigStore.Load(paths.ConfigFile);
        Manifest = ComponentManifest.LoadEmbedded();
        Logs = new LogHub();

        Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalXI", AppVersion));

        Installer = new ComponentInstaller(paths, Http, Manifest.Repository);
        MariaDb = new MariaDbServer(paths, Logs);
        Game = new GameLauncher(paths);
    }

    public static string AppVersion => typeof(LauncherService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public AppPaths Paths { get; }
    public LauncherConfig Config { get; }
    public ComponentManifest Manifest { get; }
    public LogHub Logs { get; }
    public HttpClient Http { get; }
    public ComponentInstaller Installer { get; }
    public MariaDbServer MariaDb { get; }
    public GameLauncher Game { get; }

    public GameDatabase Database => new(Paths, Config.Database);

    public string ServerDirectory =>
        string.IsNullOrWhiteSpace(Config.Server.OverrideDirectory) ? Paths.ServerDir : Config.Server.OverrideDirectory;

    public ServerPackage ServerPackage => _package ??= ServerPackage.Load(ServerDirectory);

    public LsbServer Server => _server ??= CreateServer();

    public SaveManager Saves => new(Paths, Database, MariaDb, ServerPackage);

    public LoginClient LoginClient => LoginClient.ForXiloaderVersion(Manifest["xiloader"].Version);

    /// <summary>The address the game's loader connects to.</summary>
    public string GameServerAddress => Config.Mode switch
    {
        PlayMode.Join => Config.Join.ServerAddress,
        // The host plays through the same address as friends so the login and zone connections come from one IP.
        PlayMode.Host => Config.Host.AdvertisedIp,
        _ => "127.0.0.1",
    };

    public void SaveConfig() => ConfigStore.Save(Paths.ConfigFile, Config);

    /// <summary>Call after changing the mode, database or server folder so cached objects pick it up.</summary>
    public void Reload()
    {
        _package = null;
        _server = null;
    }

    // ---------------------------------------------------------------- setup

    public IReadOnlyList<ComponentSpec> RequiredComponents() =>
        Manifest.ForRole(includeServer: Config.RunsLocalServer)
            .Where(c => c.Id is not ("server" or "meshes") || string.IsNullOrWhiteSpace(Config.Server.OverrideDirectory))
            .ToList();

    public IReadOnlyList<ComponentSpec> MissingComponents() => RequiredComponents().Where(c => !Installer.IsInstalled(c)).ToList();

    public async Task InstallComponentsAsync(IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        if (_server?.IsRunning == true)
        {
            throw new InvalidOperationException("Stop the server before updating its files.");
        }

        foreach (var component in MissingComponents())
        {
            await Installer.InstallAsync(component, progress, cancellationToken);
        }

        Reload();
    }

    /// <summary>Creates MariaDB's data folder (first run), starts it, and loads LandSandBoat's game data if that hasn't happened yet.</summary>
    public async Task PrepareDatabaseAsync(IProgress<string>? status, IProgress<ImportProgress>? import, CancellationToken cancellationToken)
    {
        if (!MariaDb.IsInitialized)
        {
            Config.Database.Port = PortProbe.FindFreeTcpPort(DatabaseSettings.DefaultPort);
            if (string.IsNullOrEmpty(Config.Database.RootPassword))
            {
                Config.Database.RootPassword = Secrets.NewPassword();
            }

            SaveConfig();
            status?.Report("Creating the database...");
            await MariaDb.InitializeAsync(Database, cancellationToken);
        }

        status?.Report("Starting the database...");
        await MariaDb.StartAsync(Database, cancellationToken);

        var era = Config.Server.Era.ToString();
        var imported = await ImportedServerVersionAsync(cancellationToken);
        var serverCurrent = imported is not null && (ServerPackage.LsbCommit is null || imported == ServerPackage.LsbCommit);
        var eraCurrent = await ReadMetaAsync("imported_era", cancellationToken) == era;
        if (serverCurrent && eraCurrent)
        {
            return; // Game data matches the installed server and the chosen era.
        }

        if (!ServerPackage.LooksValid)
        {
            throw new InvalidOperationException($"No LandSandBoat server was found in {ServerDirectory}.");
        }

        var database = Database;
        var importer = new SchemaImporter(database, MariaDb.Tool("mariadb"), ServerDirectory);
        if (imported is null)
        {
            await database.CreateDatabaseAsync(cancellationToken);
            status?.Report("Loading game data (a few minutes)...");
            await importer.ImportAllAsync(import, cancellationToken);
        }
        else
        {
            // A newer server or a different era: reload game data, keep every player table.
            status?.Report(serverCurrent ? "Switching game era - backing up your characters first..." : "Server updated - backing up your characters first...");
            await Saves.BackupAsync(serverCurrent ? "before-era-change" : "before-update", Config.Account.Username, Config.Backups.Keep, cancellationToken);
            status?.Report("Reloading game data...");
            await importer.RefreshGameDataAsync(ServerPackage.PlayerTables, import, cancellationToken);
        }

        // Era data packs edit game data in place, so they go on after every fresh load of it.
        var eraFiles = importer.EraSqlFiles(EraPresets.SqlPacks(Config.Server.Era));
        if (eraFiles.Count > 0)
        {
            status?.Report("Applying classic-era game data...");
            var skipped = await importer.ApplyEraPacksAsync(eraFiles, import, cancellationToken);
            foreach (var problem in skipped)
            {
                Logs.Add("launcher", "Skipped part of a LandSandBoat era data pack (out of date upstream): " + problem);
            }
        }

        await WriteMetaAsync("imported_server", ServerPackage.LsbCommit ?? "unknown", cancellationToken);
        await WriteMetaAsync("imported_era", era, cancellationToken);
    }

    /// <summary>
    /// Installs anything new in components.json (a launcher update may pin a newer server) and brings the game data
    /// up to date. Does nothing when everything is current, so it is safe to run before every Play.
    /// </summary>
    public async Task UpdateIfNeededAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (MissingComponents().Count > 0)
        {
            await InstallComponentsAsync(new InlineProgress<InstallProgress>(p => progress?.Report(p.Message)), cancellationToken);
        }

        if (Config.RunsLocalServer)
        {
            await PrepareDatabaseAsync(progress, new InlineProgress<ImportProgress>(p =>
            {
                if (p.Total > 0)
                {
                    progress?.Report($"Loading game data... {p.Done} of {p.Total}");
                }
            }), cancellationToken);
        }
    }

    /// <summary>Which server build the game data was last imported from, or null if the import never finished.</summary>
    public Task<string?> ImportedServerVersionAsync(CancellationToken cancellationToken = default) =>
        ReadMetaAsync("imported_server", cancellationToken);

    /// <summary>The launcher's own bookkeeping, kept in a small table inside the game database.</summary>
    private async Task<string?> ReadMetaAsync(string name, CancellationToken cancellationToken)
    {
        var database = Database;
        if (!await database.DatabaseExistsAsync(cancellationToken) || (await database.ExistingTablesAsync([MetaTable], cancellationToken)).Count == 0)
        {
            return null;
        }

        return await database.ScalarAsync(
            $"SELECT value FROM `{MetaTable}` WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = name },
            cancellationToken) as string;
    }

    private async Task WriteMetaAsync(string name, string value, CancellationToken cancellationToken)
    {
        var database = Database;
        await database.ExecuteAsync($"CREATE TABLE IF NOT EXISTS `{MetaTable}` (`name` VARCHAR(64) PRIMARY KEY, `value` TEXT)", cancellationToken: cancellationToken);
        await database.ExecuteAsync(
            $"REPLACE INTO `{MetaTable}` (name, value) VALUES (@name, @value)",
            new Dictionary<string, object?> { ["@name"] = name, ["@value"] = value },
            cancellationToken);
    }

    /// <summary>Creates the launcher's account on the local server, or resets its password to match the launcher's.</summary>
    public Task EnsureLocalAccountAsync(CancellationToken cancellationToken) =>
        new AccountStore(Database).EnsureAsync(Config.Account.Username, Config.Account.Password, cancellationToken);

    public async Task RepairRegistryAsync(FfxiInstallation install)
    {
        if (install.PlayOnlineViewerPath is null)
        {
            throw new InvalidOperationException("PlayOnline Viewer was not found next to the game, so the registry can't be repaired automatically.");
        }

        if (!await Elevation.RunAsync(Elevation.RepairRegistry, install.FfxiPath, install.PlayOnlineViewerPath))
        {
            throw new OperationCanceledException("Registry repair was cancelled at the Windows permission prompt.");
        }
    }

    public async Task InstallFirewallRulesAsync()
    {
        if (!await Elevation.RunAsync(Elevation.AddFirewallRules, ServerDirectory))
        {
            throw new OperationCanceledException("Firewall setup was cancelled at the Windows permission prompt.");
        }
    }

    // ---------------------------------------------------------------- play

    public async Task StartServerAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!ServerPackage.LooksValid)
        {
            throw new InvalidOperationException($"The server files in {ServerDirectory} are missing or incomplete. Run setup again from Settings.");
        }

        progress?.Report("Starting the database...");
        await MariaDb.StartAsync(Database, cancellationToken);

        // Only manage the module list in the downloaded server; a developer's own build keeps theirs.
        if (string.IsNullOrWhiteSpace(Config.Server.OverrideDirectory))
        {
            EraPresets.WriteModuleList(ServerDirectory);
        }

        var era = EraPresets.Settings(Config.Server.Era);
        var options = Config.Mode == PlayMode.Host
            ? new ServerStartOptions(CheckHostAddress(), LocalOnly: false, Config.Server.LazyZones, Config.Server.AllowOlderClients, era)
            : new ServerStartOptions("127.0.0.1", LocalOnly: true, Config.Server.LazyZones, Config.Server.AllowOlderClients, era);
        await Server.StartAsync(options, progress, cancellationToken);
    }

    public async Task PlayAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (Game.IsGameRunning)
        {
            throw new InvalidOperationException("The game is already running.");
        }

        if (!Config.RunsLocalServer || !Server.IsRunning)
        {
            await UpdateIfNeededAsync(progress, cancellationToken);
        }

        if (Config.RunsLocalServer && !Server.IsRunning)
        {
            await StartServerAsync(progress, cancellationToken);
        }

        progress?.Report("Launching the game...");
        Game.WriteBootConfig(GameServerAddress, Config.Account, Config.Window);
        Game.Launch(Config.Window.RunAsAdministrator);
    }

    /// <summary>Called once the game window closes: back up and, unless friends are still online, shut the server down.</summary>
    public async Task AfterGameClosedAsync(IProgress<string>? progress)
    {
        if (!Config.RunsLocalServer || _server is not { IsRunning: true } server)
        {
            return;
        }

        // The map server notices the player left after a short timeout and saves them; wait for that.
        progress?.Report("Waiting for the server to save your character...");
        for (var i = 0; i < 30 && await server.OnlinePlayersAsync() is > 0; i++)
        {
            await Task.Delay(2000);
        }

        if (Config.Mode == PlayMode.Host && await server.OnlinePlayersAsync() is > 0)
        {
            progress?.Report("Friends are still online - the server keeps running.");
            return;
        }

        if (Config.Server.StopWhenGameCloses)
        {
            await StopServerAsync(progress);
        }
        else if (Config.Backups.OnGameExit)
        {
            await BackupAsync("auto", progress);
        }
    }

    public async Task StopServerAsync(IProgress<string>? progress, bool backup = true)
    {
        if (_server is { } server)
        {
            await server.StopAsync(progress);
        }

        if (backup && Config.Backups.OnGameExit && Config.SetupComplete && await Database.CanConnectAsync())
        {
            await BackupAsync("auto", progress);
        }

        progress?.Report("Stopping the database...");
        await MariaDb.StopAsync(Database);
        progress?.Report("Server stopped.");
    }

    // ---------------------------------------------------------------- saves

    public async Task<string?> BackupAsync(string reason, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Backing up your characters...");
        try
        {
            return await Saves.BackupAsync(reason, Config.Account.Username, Config.Backups.Keep, cancellationToken);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            Logs.Add("launcher", "Backup failed: " + e.Message);
            return null;
        }
    }

    public async Task<SaveManifest> ExportSaveAsync(string destination, CancellationToken cancellationToken = default)
    {
        await MariaDb.StartAsync(Database, cancellationToken);
        return await Saves.ExportAsync(destination, "export", Config.Account.Username, cancellationToken);
    }

    /// <summary>
    /// Replaces this PC's characters with the ones in a save. The server is stopped first and the current state is
    /// backed up, so an import can always be undone by importing that backup.
    /// </summary>
    public async Task<SaveManifest> ImportSaveAsync(string saveFile, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        if (_server is { IsRunning: true } server)
        {
            progress?.Report("Stopping the server...");
            await server.StopAsync(progress);
        }

        await MariaDb.StartAsync(Database, cancellationToken);
        progress?.Report("Backing up your current characters first...");
        await Saves.BackupAsync("before-import", Config.Account.Username, Config.Backups.Keep, cancellationToken);

        progress?.Report("Loading the save...");
        var manifest = await Saves.ImportAsync(saveFile, cancellationToken);

        // The save brings its own accounts. Log in as the account that owned it, with this PC's password,
        // so the imported characters show up straight away.
        var accounts = new AccountStore(Database);
        if (manifest.PrimaryAccount is { Length: > 0 } owner && await accounts.ExistsAsync(owner, cancellationToken))
        {
            Config.Account.Username = owner;
            SaveConfig();
        }

        await EnsureLocalAccountAsync(cancellationToken);
        progress?.Report("Save loaded.");
        return manifest;
    }

    // ---------------------------------------------------------------- helpers

    private string CheckHostAddress()
    {
        var address = Config.Host.AdvertisedIp;
        if (string.IsNullOrWhiteSpace(address) || !PortProbe.IsLocalAddress(address))
        {
            throw new InvalidOperationException(
                $"The hosting address \"{address}\" isn't on this PC right now. If you use Radmin VPN, make sure it's " +
                "connected, or choose another address on the Network page.");
        }

        return address;
    }

    private LsbServer CreateServer()
    {
        var server = new LsbServer(ServerPackage, Database, Paths, Logs);
        server.ProcessCrashed += (name, tail) => Logs.Add("launcher", $"{name} stopped unexpectedly:\n{tail}");
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is { IsRunning: true })
        {
            await StopServerAsync(null);
        }
        else if (MariaDb.StartedHere)
        {
            await MariaDb.StopAsync(Database);
        }

        Http.Dispose();
    }
}
