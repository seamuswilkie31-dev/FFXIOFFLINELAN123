using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Util;
using Microsoft.Win32;

namespace LocalXI.App.ViewModels;

public sealed partial class SettingsViewModel(MainViewModel main) : PageViewModel(main)
{
    public ObservableCollection<InstallCheck> GameChecks { get; } = [];

    public IReadOnlyList<WindowMode> WindowModes { get; } = Enum.GetValues<WindowMode>();

    [ObservableProperty]
    private string _ffxiPath = "";

    [ObservableProperty]
    private string _registryStatus = "";

    [ObservableProperty]
    private bool _registryOk;

    [ObservableProperty]
    private int _width;

    [ObservableProperty]
    private int _height;

    [ObservableProperty]
    private WindowMode _windowMode;

    [ObservableProperty]
    private bool _runAsAdministrator;

    [ObservableProperty]
    private bool _lazyZones;

    [ObservableProperty]
    private bool _allowOlderClients;

    [ObservableProperty]
    private bool _stopWhenGameCloses;

    [ObservableProperty]
    private bool _backupOnGameExit;

    [ObservableProperty]
    private int _backupsToKeep;

    [ObservableProperty]
    private string _overrideDirectory = "";

    [ObservableProperty]
    private string _newPassword = "";

    public string Username => Launcher.Config.Account.Username;
    public bool RunsLocalServer => Launcher.Config.RunsLocalServer;
    public string DataFolder => Launcher.Paths.DataRoot;
    public string UserFolder => Launcher.Paths.UserRoot;
    public string Version => $"LocalXI {LauncherService.AppVersion}";

    public override void OnShown()
    {
        var config = Launcher.Config;
        FfxiPath = config.FfxiPath ?? "";
        Width = config.Window.Width;
        Height = config.Window.Height;
        WindowMode = config.Window.Mode;
        RunAsAdministrator = config.Window.RunAsAdministrator;
        LazyZones = config.Server.LazyZones;
        AllowOlderClients = config.Server.AllowOlderClients;
        StopWhenGameCloses = config.Server.StopWhenGameCloses;
        BackupOnGameExit = config.Backups.OnGameExit;
        BackupsToKeep = config.Backups.Keep;
        OverrideDirectory = config.Server.OverrideDirectory ?? "";
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(RunsLocalServer));
        CheckGame();
    }

    [RelayCommand]
    private void CheckGame()
    {
        GameChecks.Clear();
        if (string.IsNullOrWhiteSpace(FfxiPath) || !Directory.Exists(FfxiPath))
        {
            GameChecks.Add(new InstallCheck("Game folder", false, "Choose your FINAL FANTASY XI folder."));
            RegistryOk = false;
            RegistryStatus = "";
            return;
        }

        var install = FfxiInstallation.Inspect(FfxiPath);
        foreach (var check in install.Checks)
        {
            GameChecks.Add(check);
        }

        RegistryOk = PlayOnlineRegistry.Matches(install);
        RegistryStatus = RegistryOk
            ? "✓ Windows knows where the game is installed."
            : "Windows doesn't have this install location registered. The game needs it to start.";
    }

    [RelayCommand]
    private void BrowseGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select your FINAL FANTASY XI folder" };
        if (dialog.ShowDialog() == true)
        {
            FfxiPath = dialog.FolderName;
            CheckGame();
        }
    }

    [RelayCommand]
    private async Task FixRegistryAsync()
    {
        var install = FfxiInstallation.Inspect(FfxiPath);
        await Main.RunAsync(async progress =>
        {
            progress.Report("Updating the registry (approve the Windows prompt)...");
            await Launcher.RepairRegistryAsync(install);
            progress.Report("Registry updated.");
        });
        CheckGame();
    }

    [RelayCommand]
    private void BrowseOverride()
    {
        var dialog = new OpenFolderDialog { Title = "Select a LandSandBoat folder containing xi_map.exe" };
        if (dialog.ShowDialog() == true)
        {
            OverrideDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var config = Launcher.Config;
        config.FfxiPath = FfxiPath;
        config.Window.Width = Math.Clamp(Width, 640, 7680);
        config.Window.Height = Math.Clamp(Height, 480, 4320);
        config.Window.Mode = WindowMode;
        config.Window.RunAsAdministrator = RunAsAdministrator;
        config.Server.LazyZones = LazyZones;
        config.Server.AllowOlderClients = AllowOlderClients;
        config.Server.StopWhenGameCloses = StopWhenGameCloses;
        config.Backups.OnGameExit = BackupOnGameExit;
        config.Backups.Keep = Math.Clamp(BackupsToKeep, 1, 100);

        var overrideDir = string.IsNullOrWhiteSpace(OverrideDirectory) ? null : OverrideDirectory.Trim();
        var serverChanged = overrideDir != config.Server.OverrideDirectory;
        config.Server.OverrideDirectory = overrideDir;

        Launcher.SaveConfig();
        if (serverChanged)
        {
            Launcher.Reload();
        }

        Main.Status = Launcher.Config.RunsLocalServer && Launcher.Server.IsRunning
            ? "Settings saved. Server settings apply the next time the server starts."
            : "Settings saved.";
        OnShown();
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (!Secrets.IsValidPassword(NewPassword))
        {
            MessageBox.Show("Passwords are 6-32 characters without spaces or quotes.", "Password", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Launcher.Config.RunsLocalServer)
        {
            MessageBox.Show(
                "While joining a friend, your account lives on their server. Ask them to reset it on their Network page, then enter the new password here.",
                "Password", MessageBoxButton.OK, MessageBoxImage.Information);
            Launcher.Config.Account.Password = NewPassword;
            Launcher.SaveConfig();
            NewPassword = "";
            return;
        }

        await Main.RunAsync(async progress =>
        {
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            await new AccountStore(Launcher.Database).SetPasswordAsync(Launcher.Config.Account.Username, NewPassword);
            Launcher.Config.Account.Password = NewPassword;
            Launcher.SaveConfig();
            progress.Report("Password changed.");
        });
        NewPassword = "";
    }

    [RelayCommand]
    private void RerunSetup()
    {
        if (Launcher.Config.RunsLocalServer && Launcher.Server.IsRunning)
        {
            MessageBox.Show("Stop the server on the Play page first.", "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Main.StartSetup(resume: false);
    }

    [RelayCommand]
    private void OpenDataFolder() => MainViewModel.OpenFolder(Launcher.Paths.DataRoot);

    [RelayCommand]
    private void OpenUserFolder() => MainViewModel.OpenFolder(Launcher.Paths.UserRoot);
}
