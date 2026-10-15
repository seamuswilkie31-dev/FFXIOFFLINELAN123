using System.Diagnostics;
using LocalXI.Core.Database;
using LocalXI.Core.Logging;
using LocalXI.Core.Network;

namespace LocalXI.Core.Server;

public enum ServerProcessState { Stopped, Starting, Running, Crashed }

public sealed record ServerProcessStatus(string Name, string Description, ServerProcessState State);

/// <param name="AdvertisedIp">
/// The address clients are told to use for zones. 127.0.0.1 for solo play; the host's LAN or VPN (e.g. Radmin) address when hosting.
/// </param>
/// <param name="LocalOnly">Bind the login server to 127.0.0.1 so nothing outside this PC can reach it.</param>
/// <param name="Settings">Extra XI_* setting overrides, such as an era preset.</param>
public sealed record ServerStartOptions(
    string AdvertisedIp,
    bool LocalOnly,
    bool LazyZones,
    bool AllowOlderClients,
    IReadOnlyDictionary<string, string>? Settings = null);

public sealed class PortsInUseException(IReadOnlyList<string> ports)
    : Exception($"These ports are already in use: {string.Join(", ", ports)}. Another FFXI server may be running - close it and try again.")
{
    public IReadOnlyList<string> Ports { get; } = ports;
}

/// <summary>Starts, watches and stops LandSandBoat's server programs.</summary>
public sealed class LsbServer(ServerPackage package, GameDatabase database, AppPaths paths, LogHub? logs = null)
{
    public const int MapPort = 54230;

    /// <param name="ReadyGrace">
    /// How long after its ports open to wait for the program's "ready to work" line before carrying on anyway
    /// (in case a future version stops printing it).
    /// </param>
    private sealed record Definition(string Name, string Description, int[] TcpPorts, int[] UdpPorts, bool Optional, TimeSpan ReadyGrace, TimeSpan ReadyTimeout);

    // Start order follows LandSandBoat's docker-compose: world hosts the message broker the others connect to,
    // and the map server comes last because it depends on all of them. Loading every zone can take the map
    // server well over a minute.
    private static readonly Definition[] Definitions =
    [
        new("xi_world", "World", [54003], [], Optional: false, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
        new("xi_connect", "Login", [54001, 54230, 54231], [], Optional: false, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
        new("xi_search", "Search", [54002], [], Optional: false, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
        new("xi_profile", "Profile", [51220], [], Optional: true, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
        new("xi_map", "Map", [], [MapPort], Optional: false, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5)),
    ];

    private readonly List<ManagedProcess> _running = [];
    private readonly Dictionary<string, ServerProcessState> _states = Definitions.ToDictionary(d => d.Name, _ => ServerProcessState.Stopped);
    private bool _stopping;

    public event Action? StatusChanged;

    /// <summary>Raised when a server program exits on its own while the server is supposed to be running.</summary>
    public event Action<string, string>? ProcessCrashed;

    public bool IsRunning => _running.Count > 0 && _running.All(p => !p.HasExited);

    public IReadOnlyList<ServerProcessStatus> Status =>
        Definitions
            .Where(d => !d.Optional || File.Exists(package.Executable(d.Name)))
            .Select(d => new ServerProcessStatus(d.Name, d.Description, _states[d.Name]))
            .ToList();

    public async Task StartAsync(ServerStartOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        _stopping = false;
        StopLeftovers();

        var busy = Definitions
            .Where(d => !d.Optional || File.Exists(package.Executable(d.Name)))
            .SelectMany(d => d.TcpPorts.Where(PortProbe.IsTcpListening).Select(p => $"TCP {p}")
                .Concat(d.UdpPorts.Where(PortProbe.IsUdpBound).Select(p => $"UDP {p}")))
            .Distinct()
            .ToList();
        if (busy.Count > 0)
        {
            throw new PortsInUseException(busy);
        }

        // Clients are sent to whatever address zone_settings holds, and xi_map only serves zones whose
        // address matches its --ip argument, so both must agree. Disabled zones (port 0) stay disabled.
        progress?.Report($"Pointing zones at {options.AdvertisedIp}...");
        await database.ExecuteAsync(
            "UPDATE zone_settings SET zoneip = @ip WHERE zoneport <> 0",
            new Dictionary<string, object?> { ["@ip"] = options.AdvertisedIp },
            cancellationToken);

        var environment = BuildEnvironment(options);
        try
        {
            foreach (var definition in Definitions)
            {
                var executable = package.Executable(definition.Name);
                if (!File.Exists(executable))
                {
                    if (definition.Optional)
                    {
                        continue;
                    }

                    throw new FileNotFoundException($"{definition.Name}.exe is missing from the server folder.", executable);
                }

                progress?.Report(definition.Name == "xi_map"
                    ? "Loading the world (this can take a minute or two)..."
                    : $"Starting the {definition.Description} server...");
                SetState(definition.Name, ServerProcessState.Starting);

                var process = ManagedProcess.Start(
                    definition.Name,
                    executable,
                    Arguments(definition, options),
                    package.Directory,
                    environment,
                    Path.Combine(paths.LogsDir, definition.Name + ".log"),
                    logs);
                process.Exited += OnProcessExited;
                _running.Add(process);

                await WaitUntilReadyAsync(process, definition, cancellationToken);
                SetState(definition.Name, ServerProcessState.Running);
            }
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync(IProgress<string>? progress = null)
    {
        _stopping = true;

        // Reverse start order: the map server saves characters while world and login are still there to receive it.
        foreach (var process in Enumerable.Reverse(_running).ToList())
        {
            progress?.Report($"Stopping {process.Name}...");
            await process.StopAsync(process.Name == "xi_map" ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(5));
            SetState(process.Name, ServerProcessState.Stopped);
            process.Dispose();
        }

        _running.Clear();
        foreach (var name in _states.Keys.ToList())
        {
            SetState(name, ServerProcessState.Stopped);
        }
    }

    /// <summary>How many players are logged in right now, or null if that can't be read.</summary>
    public async Task<int?> OnlinePlayersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Convert.ToInt32(await database.ScalarAsync("SELECT COUNT(*) FROM accounts_sessions", cancellationToken: cancellationToken));
        }
        catch (Exception e) when (e is MySqlConnector.MySqlException or InvalidCastException)
        {
            return null;
        }
    }

    private Dictionary<string, string> BuildEnvironment(ServerStartOptions options)
    {
        // LandSandBoat applies XI_<FILE>_<SETTING> environment variables over its settings/*.lua files,
        // so the launcher never edits the server's own config.
        var bind = options.LocalOnly ? "127.0.0.1" : "0.0.0.0";
        var environment = new Dictionary<string, string>
        {
            ["XI_NETWORK_SQL_HOST"] = "127.0.0.1",
            ["XI_NETWORK_SQL_PORT"] = database.Settings.Port.ToString(),
            ["XI_NETWORK_SQL_LOGIN"] = "root",
            ["XI_NETWORK_SQL_PASSWORD"] = database.Settings.RootPassword,
            ["XI_NETWORK_SQL_DATABASE"] = database.Settings.Name,
            ["XI_NETWORK_ZMQ_IP"] = "127.0.0.1",
            ["XI_NETWORK_LOGIN_DATA_IP"] = bind,
            ["XI_NETWORK_LOGIN_VIEW_IP"] = bind,
            ["XI_NETWORK_LOGIN_AUTH_IP"] = bind,
        };

        if (options.AllowOlderClients)
        {
            environment["XI_LOGIN_VER_LOCK"] = "0";
        }

        foreach (var (key, value) in options.Settings ?? new Dictionary<string, string>())
        {
            environment[key] = value;
        }

        return environment;
    }

    private static IEnumerable<string> Arguments(Definition definition, ServerStartOptions options)
    {
        if (definition.Name != "xi_map")
        {
            yield break;
        }

        yield return "--ip";
        yield return options.AdvertisedIp;
        yield return "--port";
        yield return MapPort.ToString();
        if (options.LazyZones)
        {
            yield return "--lazy";
        }
    }

    private static async Task WaitUntilReadyAsync(ManagedProcess process, Definition definition, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < definition.ReadyTimeout)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The {definition.Description} server ({definition.Name}) stopped during start-up (exit code {process.ExitCode}). The end of its log says:\n{process.Tail()}");
            }

            // Ports open early, so also wait for the program's own "ready to work" line.
            var listening = definition.TcpPorts.All(PortProbe.IsTcpListening) && definition.UdpPorts.All(PortProbe.IsUdpBound);
            if (listening && (process.IsReady || elapsed.Elapsed > definition.ReadyGrace))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        // Still alive but not ready where we expected: carry on rather than fail on an upstream change.
    }

    private void OnProcessExited(ManagedProcess process)
    {
        if (_stopping)
        {
            return;
        }

        SetState(process.Name, ServerProcessState.Crashed);
        ProcessCrashed?.Invoke(process.Name, process.Tail());
    }

    /// <summary>Stops server programs from this install left running by a launcher that crashed.</summary>
    private void StopLeftovers()
    {
        foreach (var definition in Definitions)
        {
            foreach (var process in Process.GetProcessesByName(definition.Name))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (path is not null && path.StartsWith(package.Directory, StringComparison.OrdinalIgnoreCase))
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(5000);
                        }
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // Not ours, or already gone.
                    }
                }
            }
        }
    }

    private void SetState(string name, ServerProcessState state)
    {
        _states[name] = state;
        StatusChanged?.Invoke();
    }
}
