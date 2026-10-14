using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalXI.Core;
using LocalXI.Core.Config;

namespace LocalXI.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(LauncherService launcher)
    {
        Launcher = launcher;
        Logs = new LogsViewModel(this);
        Play = new PlayViewModel(this);
        Saves = new SavesViewModel(this);
        Network = new NetworkViewModel(this);
        Settings = new SettingsViewModel(this);

        CurrentPageName = "Play";
        RefreshModeSummary();
        if (!launcher.Config.SetupComplete)
        {
            StartSetup(resume: false);
        }
    }

    public LauncherService Launcher { get; }
    public PlayViewModel Play { get; }
    public SavesViewModel Saves { get; }
    public NetworkViewModel Network { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }

    [ObservableProperty]
    private string _currentPageName = "";

    [ObservableProperty]
    private PageViewModel? _currentPage;

    /// <summary>Non-null while the setup wizard covers the window.</summary>
    [ObservableProperty]
    private SetupViewModel? _setup;

    [ObservableProperty]
    private string _modeSummary = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    partial void OnCurrentPageNameChanged(string value)
    {
        CurrentPage = value switch
        {
            "Saves" => Saves,
            "Network" => Network,
            "Settings" => Settings,
            "Logs" => Logs,
            _ => Play,
        };
        CurrentPage.OnShown();
    }

    public void StartSetup(bool resume) => Setup = new SetupViewModel(this, resume);

    public void FinishSetup()
    {
        Setup = null;
        Launcher.Reload();
        RefreshModeSummary();
        CurrentPageName = "Play";
        CurrentPage?.OnShown();
    }

    public void RefreshModeSummary() => ModeSummary = Launcher.Config.Mode switch
    {
        PlayMode.Host => $"Hosting for friends\n{Launcher.Config.Host.AdvertisedIp}",
        PlayMode.Join => $"Joining a friend\n{Launcher.Config.Join.ServerAddress}",
        _ => "Playing solo",
    };

    /// <summary>
    /// Runs one long operation at a time (Play, Stop, Import...), reporting progress in the status bar and showing
    /// any failure as a message rather than crashing.
    /// </summary>
    public async Task<bool> RunAsync(Func<IProgress<string>, Task> work)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        var progress = new Progress<string>(message =>
        {
            Status = message;
            Launcher.Logs.Add("launcher", message);
        });

        try
        {
            await work(progress);
            return true;
        }
        catch (OperationCanceledException e)
        {
            Status = e.Message;
            return false;
        }
        catch (Exception e)
        {
            Status = "Something went wrong.";
            Launcher.Logs.Add("launcher", e.ToString());
            MessageBox.Show(e.Message, "LocalXI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            IsBusy = false;
            Play.RefreshServerStatus();
        }
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
    }
}

/// <summary>Base for the sidebar pages.</summary>
public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    protected MainViewModel Main { get; } = main;
    protected LauncherService Launcher => Main.Launcher;

    /// <summary>Called each time the page is navigated to, to refresh anything that may have changed.</summary>
    public virtual void OnShown()
    {
    }
}
