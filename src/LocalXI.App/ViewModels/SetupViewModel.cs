using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core.Components;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Network;
using LocalXI.Core.Util;
using Microsoft.Win32;

namespace LocalXI.App.ViewModels;

public enum SetupStep { Mode, Game, Account, Install }

public enum SetupTaskState { Pending, Running, Done, Failed }

public sealed partial class SetupTask(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private SetupTaskState _state;

    [ObservableProperty]
    private string _detail = "";
}

/// <summary>First-run wizard: choose how to play, find the game, pick a login, then download and set everything up.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SetupViewModel(MainViewModel main, bool resume)
    {
        _main = main;
        var config = main.Launcher.Config;

        CanCancel = config.SetupComplete;
        Mode = config.Mode;
        FfxiPath = config.FfxiPath ?? FfxiLocator.FindBest() ?? "";
        Username = config.Account.Username;
        Password = config.Account.Password;
        ConfirmPassword = config.Account.Password;
        JoinAddress = config.Join.ServerAddress;
        OverrideDirectory = config.Server.OverrideDirectory ?? "";

        foreach (var address in NetworkInfo.GetHostAddresses())
        {
            Addresses.Add(address);
        }

        SelectedAddress = Addresses.FirstOrDefault(a => a.Address == config.Host.AdvertisedIp) ?? NetworkInfo.Suggest();
        CheckGame();

        // Resuming (e.g. after switching to hosting) only needs the download/setup step.
        if (resume && config.FfxiPath is not null && config.Account.Username.Length > 0)
        {
            Step = SetupStep.Install;
            _ = InstallAsync();
        }
    }

    public bool CanCancel { get; }

    // ---------------------------------------------------------------- navigation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber), nameof(IsModeStep), nameof(IsGameStep), nameof(IsAccountStep), nameof(IsInstallStep))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private SetupStep _step;

    public int StepNumber => (int)Step + 1;
    public bool IsModeStep => Step == SetupStep.Mode;
    public bool IsGameStep => Step == SetupStep.Game;
    public bool IsAccountStep => Step == SetupStep.Account;
    public bool IsInstallStep => Step == SetupStep.Install;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack() => Step is > SetupStep.Mode and < SetupStep.Install;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (Step == SetupStep.Account)
        {
            Step = SetupStep.Install;
            _ = InstallAsync();
            return;
        }

        Step++;
    }

    private bool CanGoNext() => Step switch
    {
        SetupStep.Mode => true,
        SetupStep.Game => GameValid,
        SetupStep.Account => AccountError.Length == 0,
        _ => false,
    };

    [RelayCommand]
    private void Cancel() => _main.Setup = null;

    // ---------------------------------------------------------------- step 1: mode

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHost), nameof(IsJoin), nameof(AccountIntro), nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private PlayMode _mode;

    public bool IsHost => Mode == PlayMode.Host;
    public bool IsJoin => Mode == PlayMode.Join;

    // ---------------------------------------------------------------- step 2: game folder

    public ObservableCollection<InstallCheck> GameChecks { get; } = [];

    [ObservableProperty]
    private string _ffxiPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _gameValid;

    [ObservableProperty]
    private bool _registryOk;

    [ObservableProperty]
    private string _clientAgeWarning = "";

    partial void OnFfxiPathChanged(string value) => CheckGame();

    private void CheckGame()
    {
        GameChecks.Clear();
        ClientAgeWarning = "";
        if (string.IsNullOrWhiteSpace(FfxiPath) || !Directory.Exists(FfxiPath))
        {
            GameValid = false;
            RegistryOk = true; // nothing to fix until there's a folder
            return;
        }

        var install = FfxiInstallation.Inspect(FfxiPath);
        foreach (var check in install.Checks)
        {
            GameChecks.Add(check);
        }

        GameValid = install.IsValid;
        RegistryOk = !install.IsValid || PlayOnlineRegistry.Matches(install);

        // The server's exact expected version is only known once it's downloaded; flag clearly ancient installs now.
        if (install.ClientBuildDate is { } built && built < DateTime.Now.AddYears(-1))
        {
            ClientAgeWarning =
                $"Your game files are from {built:MMMM yyyy}, and the server needs an up-to-date game. Square Enix only " +
                "lets PlayOnline update the game after it has signed in to the official servers once (an existing account, " +
                "a bought copy or Square Enix's free trial all work). After that, PlayOnline's Check Files keeps it updated " +
                "even without a subscription. You can finish setup now and update afterwards; the README explains the steps.";
        }
    }

    [RelayCommand]
    private void BrowseGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select your FINAL FANTASY XI folder" };
        if (dialog.ShowDialog() == true)
        {
            FfxiPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task FixRegistryAsync()
    {
        var install = FfxiInstallation.Inspect(FfxiPath);
        await _main.RunAsync(async progress =>
        {
            progress.Report("Updating the registry (approve the Windows prompt)...");
            await _main.Launcher.RepairRegistryAsync(install);
            progress.Report("Registry updated.");
        });
        CheckGame();
    }

    // ---------------------------------------------------------------- step 3: account and network

    public ObservableCollection<HostAddress> Addresses { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _username = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _password = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _confirmPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private HostAddress? _selectedAddress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _joinAddress = "";

    [ObservableProperty]
    private string _overrideDirectory = "";

    public string AccountIntro => Mode == PlayMode.Join
        ? "Choose a login for your friend's server. LocalXI creates the account on their server for you. If your friend already made you one, enter those details instead."
        : "Choose a login for your own server. It only exists on this PC (it is not your Square Enix account).";

    public string AccountError
    {
        get
        {
            if (!Secrets.IsValidUsername(Username))
            {
                return "Username: 3-16 letters, numbers or _.";
            }

            if (!Secrets.IsValidPassword(Password))
            {
                return "Password: 6-32 characters, letters, numbers and ! @ $ ^ * _ + = . , ? ~ -";
            }

            if (Password != ConfirmPassword)
            {
                return "The passwords don't match.";
            }

            if (Mode == PlayMode.Host && SelectedAddress is null)
            {
                return "Choose the address friends will connect to.";
            }

            if (Mode == PlayMode.Join && string.IsNullOrWhiteSpace(JoinAddress))
            {
                return "Enter your friend's address.";
            }

            return "";
        }
    }

    [RelayCommand]
    private void RefreshAddresses()
    {
        Addresses.Clear();
        foreach (var address in NetworkInfo.GetHostAddresses())
        {
            Addresses.Add(address);
        }

        SelectedAddress ??= NetworkInfo.Suggest();
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

    // ---------------------------------------------------------------- step 4: install

    public ObservableCollection<SetupTask> Tasks { get; } = [];

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _installFailed;

    [ObservableProperty]
    private bool _installDone;

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private string _doneMessage = "";

    [RelayCommand]
    private Task RetryAsync() => InstallAsync();

    [RelayCommand]
    private void ChangeAnswers()
    {
        InstallFailed = false;
        Step = SetupStep.Mode;
    }

    [RelayCommand]
    private void Finish() => _main.FinishSetup();

    private async Task InstallAsync()
    {
        var launcher = _main.Launcher;
        var config = launcher.Config;

        IsInstalling = true;
        InstallFailed = false;
        InstallDone = false;
        Error = "";
        Progress = 0;

        config.Mode = Mode;
        config.FfxiPath = FfxiPath;
        config.Account.Username = Username;
        config.Account.Password = Password;
        if (SelectedAddress is not null)
        {
            config.Host.AdvertisedIp = SelectedAddress.Address;
        }

        config.Join.ServerAddress = JoinAddress.Trim();
        config.Server.OverrideDirectory = string.IsNullOrWhiteSpace(OverrideDirectory) ? null : OverrideDirectory.Trim();
        if (!config.SetupComplete && Display.PrimaryResolution() is (> 0 and var width, > 0 and var height))
        {
            // Fill the screen without a title bar, instead of a 1080p default window that overflows laptops.
            (config.Window.Width, config.Window.Height) = (width, height);
            config.Window.Mode = WindowMode.BorderlessWindowed;
        }

        launcher.SaveConfig();
        launcher.Reload();

        var plan = new List<(SetupTask Task, Func<SetupTask, Task> Run)>();
        foreach (var component in launcher.MissingComponents())
        {
            var name = component.Kind == ComponentKind.Installer ? $"Install {component.Name}" : $"Download {component.Name}";
            plan.Add((new SetupTask(name), task => InstallComponentAsync(component, task)));
        }

        if (config.RunsLocalServer)
        {
            plan.Add((new SetupTask("Set up the database and game data"), PrepareDatabaseAsync));
            plan.Add((new SetupTask("Create your account"), async task =>
            {
                await launcher.EnsureLocalAccountAsync(CancellationToken.None);
                task.Detail = $"Account \"{Username}\" is ready.";
            }));
        }
        else
        {
            plan.Add((new SetupTask($"Create your account on {config.Join.ServerAddress}"), CreateRemoteAccountAsync));
        }

        Tasks.Clear();
        foreach (var (task, _) in plan)
        {
            Tasks.Add(task);
        }

        for (var i = 0; i < plan.Count; i++)
        {
            var (task, run) = plan[i];
            task.State = SetupTaskState.Running;
            try
            {
                await run(task);
                task.State = SetupTaskState.Done;
                Progress = 100.0 * (i + 1) / plan.Count;
            }
            catch (Exception e)
            {
                task.State = SetupTaskState.Failed;
                Error = e.Message;
                launcher.Logs.Add("setup", e.ToString());
                InstallFailed = true;
                IsInstalling = false;
                return;
            }
        }

        config.SetupComplete = true;
        launcher.SaveConfig();
        IsInstalling = false;
        InstallDone = true;
        DoneMessage = config.Mode switch
        {
            PlayMode.Host => $"All set! Press Play to start your server. Friends join with the address {config.Host.AdvertisedIp}. " +
                             "Visit the Network page to let them through Windows Firewall.",
            PlayMode.Join => "All set! Press Play once your friend's server is running.",
            _ => "All set! Press Play to start your adventure.",
        };
    }

    private async Task InstallComponentAsync(ComponentSpec component, SetupTask task)
    {
        var progress = new Progress<InstallProgress>(p =>
        {
            task.Detail = p.Message;
        });
        await _main.Launcher.Installer.InstallAsync(component, progress, CancellationToken.None);
        task.Detail = $"{component.Name} {component.Version} installed.";
    }

    private async Task PrepareDatabaseAsync(SetupTask task)
    {
        var status = new Progress<string>(message => task.Detail = message);
        var import = new Progress<ImportProgress>(p =>
            task.Detail = p.Total == 0 ? "Loading game data..." : $"Loading game data... {p.Done} of {p.Total} ({p.File})");
        await _main.Launcher.PrepareDatabaseAsync(status, import, CancellationToken.None);
        task.Detail = "Game data loaded.";
    }

    private async Task CreateRemoteAccountAsync(SetupTask task)
    {
        var launcher = _main.Launcher;
        var host = launcher.Config.Join.ServerAddress;
        task.Detail = $"Contacting {host}...";

        if (!await launcher.LoginClient.PingAsync(host))
        {
            throw new InvalidOperationException(
                $"Couldn't reach a server at {host}. Make sure your friend's server is running, that you're both on the same " +
                "network or Radmin VPN network, and that they allowed LocalXI through their firewall. You can also skip this " +
                "by asking your friend to create your account on their Network page, then pressing Retry.");
        }

        var reply = await launcher.LoginClient.CreateAccountAsync(host, Username, Password);
        task.Detail = reply.Result switch
        {
            LoginResult.SuccessCreate => $"Account \"{Username}\" created on {host}.",
            LoginResult.ErrorCreateTaken => $"\"{Username}\" already exists on {host} - if it's yours, you're good to go.",
            LoginResult.ErrorCreateDisabled => $"{host} doesn't allow creating accounts here, so your friend must create \"{Username}\" for you on their Network page.",
            _ => throw new InvalidOperationException(reply.Describe()),
        };
    }
}
