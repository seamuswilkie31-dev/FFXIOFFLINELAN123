using System.Text.RegularExpressions;

namespace LocalXI.Core.Logging;

public sealed record LogLine(DateTime Time, string Source, string Text);

/// <summary>Collects output from every process the launcher runs so the UI can show one combined log.</summary>
public sealed class LogHub
{
    private const int Capacity = 5000;
    private readonly Queue<LogLine> _lines = new();
    private readonly Lock _gate = new();

    public event Action<LogLine>? LineAdded;

    public void Add(string source, string text)
    {
        var line = new LogLine(DateTime.Now, source, text);
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > Capacity)
            {
                _lines.Dequeue();
            }
        }

        LineAdded?.Invoke(line);
    }

    public IReadOnlyList<LogLine> Snapshot()
    {
        lock (_gate)
        {
            return _lines.ToList();
        }
    }
}

/// <summary>Writes one process's output to its log file and forwards it to the <see cref="LogHub"/>.</summary>
public sealed partial class LogSink : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly LogHub? _hub;
    private readonly string _source;
    private readonly Lock _gate = new();
    private bool _disposed;

    public LogSink(string path, string source, LogHub? hub)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _source = source;
        _hub = hub;
        Write($"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----");
    }

    public void Write(string? line)
    {
        if (line is null)
        {
            return;
        }

        line = AnsiEscape().Replace(line, "");
        lock (_gate)
        {
            if (!_disposed)
            {
                _writer.WriteLine(line);
            }
        }

        _hub?.Add(_source, line);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }

    public static string Tail(string path, int lines = 15)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Split('\n');
            return string.Join("\n", all.TakeLast(lines)).Trim();
        }
        catch (IOException)
        {
            return "(log unavailable)";
        }
    }

    // LandSandBoat colours its console output; the escape codes are noise in a text box.
    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
