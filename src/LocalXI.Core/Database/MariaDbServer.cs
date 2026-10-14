using System.Diagnostics;
using LocalXI.Core.Logging;
using LocalXI.Core.Util;

namespace LocalXI.Core.Database;

/// <summary>Runs the portable MariaDB from the runtime folder. Nothing is installed as a Windows service.</summary>
public sealed class MariaDbServer(AppPaths paths, LogHub? logs = null)
{
    // MariaDB renamed its tools; older zips only ship the mysql* names.
    private static readonly Dictionary<string, string> LegacyNames = new()
    {
        ["mariadbd"] = "mysqld",
        ["mariadb"] = "mysql",
        ["mariadb-dump"] = "mysqldump",
        ["mariadb-admin"] = "mysqladmin",
        ["mariadb-install-db"] = "mysql_install_db",
    };

    private Process? _process;
    private LogSink? _log;

    public string ConfigFile => Path.Combine(paths.DatabaseDir, "my.ini");
    public string LogFile => Path.Combine(paths.LogsDir, "mariadb.log");
    public bool IsInitialized => File.Exists(ConfigFile);

    /// <summary>True while a MariaDB process started by this launcher session is running.</summary>
    public bool StartedHere => _process is { HasExited: false };

    public string Tool(string name)
    {
        var bin = Path.Combine(paths.MariaDbDir, "bin");
        var path = Path.Combine(bin, name + ".exe");
        if (File.Exists(path))
        {
            return path;
        }

        if (LegacyNames.TryGetValue(name, out var legacy) && File.Exists(Path.Combine(bin, legacy + ".exe")))
        {
            return Path.Combine(bin, legacy + ".exe");
        }

        throw new FileNotFoundException($"MariaDB's {name}.exe was not found in {bin}. Try repairing the install from Settings.");
    }

    /// <summary>Creates the data folder with a root password, then locks the server to this PC only.</summary>
    public async Task InitializeAsync(GameDatabase database, CancellationToken cancellationToken)
    {
        if (IsInitialized)
        {
            return;
        }

        var dataDir = paths.DatabaseDir;
        if (Directory.Exists(dataDir) && Directory.EnumerateFileSystemEntries(dataDir).Any())
        {
            throw new InvalidOperationException($"The database folder already contains files but no my.ini:\n{dataDir}\nMove it aside and run setup again.");
        }

        var result = await ProcessRunner.RunAsync(
            Tool("mariadb-install-db"),
            [$"--datadir={dataDir}", $"--port={database.Settings.Port}", $"--password={database.Settings.RootPassword}"],
            cancellationToken: cancellationToken);
        if (!result.Success || !IsInitialized)
        {
            throw new InvalidOperationException("Creating the MariaDB database failed:\n" + result.CombinedOutput);
        }

        var ini = IniFile.Load(ConfigFile);
        // The database never needs to be reachable from other machines; friends talk to the game server, not MariaDB.
        ini.Set("mysqld", "bind-address", "127.0.0.1");
        ini.Set("mysqld", "character-set-server", "utf8mb4");
        ini.Set("mysqld", "collation-server", "utf8mb4_general_ci");
        ini.Set("mysqld", "max_allowed_packet", "64M");
        ini.Save(ConfigFile);
    }

    public async Task StartAsync(GameDatabase database, CancellationToken cancellationToken)
    {
        if (await database.CanConnectAsync(cancellationToken))
        {
            return; // Still running from an earlier session.
        }

        _log?.Dispose();
        _log = new LogSink(LogFile, "database", logs);

        var startInfo = new ProcessStartInfo(Tool("mariadbd"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add($"--defaults-file={ConfigFile}"); // must be the first option
        startInfo.ArgumentList.Add("--console");

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start MariaDB.");
        var log = _log;
        process.OutputDataReceived += (_, e) => log.Write(e.Data);
        process.ErrorDataReceived += (_, e) => log.Write(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("MariaDB stopped while starting. The end of its log says:\n" + LogSink.Tail(LogFile));
            }

            if (await database.CanConnectAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"MariaDB did not start within a minute. See {LogFile}");
    }

    public async Task StopAsync(GameDatabase database)
    {
        if (await database.CanConnectAsync())
        {
            // A clean shutdown lets InnoDB flush; this also stops an instance left over from an earlier session.
            await ProcessRunner.RunAsync(Tool("mariadb-admin"), [$"--defaults-extra-file={database.WriteClientConfig()}", "shutdown"]);
        }

        if (_process is { } process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                ProcessRunner.TryKill(process);
            }

            process.Dispose();
            _process = null;
        }

        _log?.Dispose();
        _log = null;
    }
}
