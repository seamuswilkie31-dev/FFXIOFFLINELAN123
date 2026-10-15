using System.Diagnostics;
using LocalXI.Core.Logging;
using LocalXI.Core.Util;

namespace LocalXI.Core.Server;

/// <summary>A long-running server program with its console hidden and its output captured to a log.</summary>
public sealed class ManagedProcess : IDisposable
{
    // Every LandSandBoat program logs "The <name>-server is ready to work after N seconds..." once it has
    // finished starting (Application::markLoaded). Its ports open well before that.
    private const string ReadyMarker = "is ready to work";

    private readonly Process _process;
    private readonly LogSink _log;
    private volatile bool _ready;

    private ManagedProcess(string name, Process process, LogSink log, string logFile)
    {
        Name = name;
        _process = process;
        _log = log;
        LogFile = logFile;
    }

    public string Name { get; }
    public string LogFile { get; }
    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    /// <summary>True once the program has reported that it finished starting.</summary>
    public bool IsReady => _ready;

    public event Action<ManagedProcess>? Exited;

    public static ManagedProcess Start(
        string name,
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string logFile,
        LogHub? logs)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var log = new LogSink(logFile, name, logs);
        log.Write($"> {Path.GetFileName(executable)} {string.Join(' ', startInfo.ArgumentList)}");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var managed = new ManagedProcess(name, process, log, logFile);
        process.OutputDataReceived += (_, e) => managed.OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) => managed.OnOutput(e.Data);
        process.Exited += (_, _) => managed.Exited?.Invoke(managed);
        if (!process.Start())
        {
            log.Dispose();
            throw new InvalidOperationException($"Could not start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return managed;
    }

    private void OnOutput(string? line)
    {
        _log.Write(line);
        if (!_ready && line is not null && line.Contains(ReadyMarker, StringComparison.Ordinal))
        {
            _ready = true;
        }
    }

    /// <summary>Asks the program to shut down cleanly, then forces it if it hasn't exited within <paramref name="grace"/>.</summary>
    public async Task StopAsync(TimeSpan grace)
    {
        if (_process.HasExited)
        {
            return;
        }

        if (ConsoleSignals.TrySendCtrlC(_process.Id))
        {
            using var timeout = new CancellationTokenSource(grace);
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
                return;
            }
            catch (OperationCanceledException)
            {
                _log.Write($"{Name} did not exit within {grace.TotalSeconds:0}s; stopping it forcefully.");
            }
        }

        ProcessRunner.TryKill(_process);
        await _process.WaitForExitAsync();
    }

    public string Tail(int lines = 15) => LogSink.Tail(LogFile, lines);

    public void Dispose()
    {
        _process.Dispose();
        _log.Dispose();
    }
}
