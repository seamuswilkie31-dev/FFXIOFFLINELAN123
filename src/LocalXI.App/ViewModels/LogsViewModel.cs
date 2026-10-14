using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core.Logging;

namespace LocalXI.App.ViewModels;

public sealed partial class LogsViewModel : PageViewModel
{
    private const string AllSources = "Everything";
    private const int MaxShownLines = 3000;

    private readonly ConcurrentQueue<LogLine> _pending = new();
    private readonly List<LogLine> _shown = [];

    public LogsViewModel(MainViewModel main) : base(main)
    {
        Launcher.Logs.LineAdded += _pending.Enqueue;

        // Server output can be hundreds of lines a second at start-up; batch it onto the UI a few times a second.
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => Flush();
        timer.Start();
    }

    public ObservableCollection<string> Sources { get; } = [AllSources];

    [ObservableProperty]
    private string _selectedSource = AllSources;

    [ObservableProperty]
    private string _text = "";

    /// <summary>Raised after new text arrives so the view can scroll to the end.</summary>
    public event Action? TextAppended;

    partial void OnSelectedSourceChanged(string value) => Rebuild();

    private void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var added = new StringBuilder();
        while (_pending.TryDequeue(out var line))
        {
            if (!Sources.Contains(line.Source))
            {
                Sources.Add(line.Source);
            }

            _shown.Add(line);
            if (Matches(line))
            {
                added.AppendLine(Format(line));
            }
        }

        if (_shown.Count > MaxShownLines)
        {
            _shown.RemoveRange(0, _shown.Count - MaxShownLines);
            Rebuild();
            return;
        }

        if (added.Length > 0)
        {
            Text += added.ToString();
            TextAppended?.Invoke();
        }
    }

    private void Rebuild()
    {
        Text = string.Concat(_shown.Where(Matches).Select(l => Format(l) + Environment.NewLine));
        TextAppended?.Invoke();
    }

    private bool Matches(LogLine line) => SelectedSource == AllSources || line.Source == SelectedSource;

    private static string Format(LogLine line) => $"{line.Time:HH:mm:ss} [{line.Source}] {line.Text}";

    [RelayCommand]
    private void Clear()
    {
        _shown.Clear();
        Text = "";
    }

    [RelayCommand]
    private void OpenLogsFolder() => MainViewModel.OpenFolder(Launcher.Paths.LogsDir);
}
