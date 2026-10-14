using System.Diagnostics;
using LocalXI.Core.Logging;
using LocalXI.Core.Util;

namespace LocalXI.Core.Server;

/// <summary>A long-running server program with its console hidden and its output captured to a log.</summary>
public sealed class ManagedProcess : IDisposable
{
    private readonly Process _process;
    private readonly LogSink _log;

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
        process.OutputDataReceived += (_, e) => log.Write(e.Data);
        process.ErrorDataReceived += (_, e) => log.Write(e.Data);
        if (!process.Start())
        {
            log.Dispose();
            throw new InvalidOperationException($"Could not start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var managed = new ManagedProcess(name, process, log, logFile);
        process.Exited += (_, _) => managed.Exited?.Invoke(managed);
        return managed;
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
