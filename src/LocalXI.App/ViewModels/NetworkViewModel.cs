using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Network;
using LocalXI.Core.Util;

namespace LocalXI.App.ViewModels;

public sealed partial class NetworkViewModel(MainViewModel main) : PageViewModel(main)
{
    public ObservableCollection<HostAddress> Addresses { get; } = [];
    public ObservableCollection<AccountInfo> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHostSelected), nameof(IsJoinSelected), nameof(ModeChanged))]
    private PlayMode _mode;

    [ObservableProperty]
    private HostAddress? _selectedAddress;

    [ObservableProperty]
    private string _joinAddress = "";

    [ObservableProperty]
    private string _firewallStatus = "";

    [ObservableProperty]
    private string _connectionResult = "";

    [ObservableProperty]
    private string _friendUsername = "";

    [ObservableProperty]
    private string _friendPassword = "";

    [ObservableProperty]
    private string _accountsNote = "";

    public bool IsHostSelected => Mode == PlayMode.Host;
    public bool IsJoinSelected => Mode == PlayMode.Join;
    public bool ModeChanged => Mode != Launcher.Config.Mode;
    public bool IsHosting => Launcher.Config.Mode == PlayMode.Host;

    public override void OnShown()
    {
        Mode = Launcher.Config.Mode;
        JoinAddress = Launcher.Config.Join.ServerAddress;
        RefreshAddresses();
        OnPropertyChanged(nameof(IsHosting));
        _ = RefreshFirewallAsync();
        _ = RefreshAccountsAsync();
    }

    [RelayCommand]
    private void RefreshAddresses()
    {
        Addresses.Clear();
        foreach (var address in NetworkInfo.GetHostAddresses())
        {
            Addresses.Add(address);
        }

        SelectedAddress = Addresses.FirstOrDefault(a => a.Address == Launcher.Config.Host.AdvertisedIp)
                          ?? Addresses.FirstOrDefault(a => a.Kind != NetworkKind.Virtual);
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var config = Launcher.Config;
        if (Mode == PlayMode.Host && SelectedAddress is null)
        {
            MessageBox.Show("Choose the address your friends will connect to.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Mode == PlayMode.Join && string.IsNullOrWhiteSpace(JoinAddress))
        {
            MessageBox.Show("Enter your friend's address (for Radmin VPN it starts with 26.).", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var addressChanged = Mode == PlayMode.Host && SelectedAddress!.Address != config.Host.AdvertisedIp;
        var serverRunning = config.RunsLocalServer && Launcher.Server.IsRunning;
        if (serverRunning && (ModeChanged || addressChanged))
        {
            if (MessageBox.Show("The server has to restart to apply this. Stop it now?", "Network", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                return;
            }

            if (!await Main.RunAsync(progress => Launcher.StopServerAsync(progress)))
            {
                return;
            }
        }

        config.Mode = Mode;
        if (SelectedAddress is not null && Mode == PlayMode.Host)
        {
            config.Host.AdvertisedIp = SelectedAddress.Address;
        }

        config.Join.ServerAddress = JoinAddress.Trim();
        Launcher.SaveConfig();
        Launcher.Reload();
        Main.RefreshModeSummary();
        Main.Status = "Network settings saved.";

        // Switching from joining to hosting or solo needs the server files and database.
        if (Launcher.MissingComponents().Count > 0 || (config.RunsLocalServer && !Launcher.MariaDb.IsInitialized))
        {
            Main.StartSetup(resume: true);
            return;
        }

        OnShown();
    }

    [RelayCommand]
    private async Task AllowThroughFirewallAsync()
    {
        await Main.RunAsync(async progress =>
        {
            progress.Report("Adding Windows Firewall rules (approve the Windows prompt)...");
            await Launcher.InstallFirewallRulesAsync();
            progress.Report("Firewall rules added.");
        });
        await RefreshFirewallAsync();
    }

    private async Task RefreshFirewallAsync()
    {
        if (!Launcher.Config.RunsLocalServer || !Launcher.ServerPackage.LooksValid)
        {
            FirewallStatus = "";
            return;
        }

        FirewallStatus = await FirewallRules.AreInstalledAsync(Launcher.ServerDirectory)
            ? "✓ Friends are allowed through Windows Firewall (home network and VPN addresses only)."
            : "Windows Firewall will block friends until you allow LocalXI through it.";
    }

    private async Task RefreshAccountsAsync()
    {
        Accounts.Clear();
        if (!IsHosting || !Launcher.Config.SetupComplete)
        {
            return;
        }

        try
        {
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            foreach (var account in await new AccountStore(Launcher.Database).ListAsync())
            {
                Accounts.Add(account);
            }

            AccountsNote = "";
        }
        catch (Exception e)
        {
            AccountsNote = "Couldn't read accounts: " + e.Message;
        }
    }

    [RelayCommand]
    private async Task CreateFriendAccountAsync()
    {
        if (!Secrets.IsValidUsername(FriendUsername) || !Secrets.IsValidPassword(FriendPassword))
        {
            MessageBox.Show(
                "Usernames are 3-16 letters, numbers or _. Passwords are 6-32 characters without spaces or quotes.",
                "Create account", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await Main.RunAsync(async progress =>
        {
            var accounts = new AccountStore(Launcher.Database);
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            if (await accounts.ExistsAsync(FriendUsername))
            {
                await accounts.SetPasswordAsync(FriendUsername, FriendPassword);
                progress.Report($"Reset the password for {FriendUsername}.");
            }
            else
            {
                await accounts.CreateAsync(FriendUsername, FriendPassword);
                progress.Report($"Created account {FriendUsername}. Your friend enters these details when they choose \"Join a friend\".");
            }
        });
        FriendPassword = "";
        await RefreshAccountsAsync();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var host = JoinAddress.Trim();
        if (host.Length == 0)
        {
            return;
        }

        ConnectionResult = $"Checking {host}...";
        ConnectionResult = await Launcher.LoginClient.PingAsync(host)
            ? $"✓ Found a LocalXI / LandSandBoat server at {host}."
            : $"✗ Couldn't reach a server at {host}. Check the address, that your friend has pressed Play (or started the server), " +
              "that you are both connected to the same Radmin VPN network, and that they allowed LocalXI through their firewall.";
    }

    [RelayCommand]
    private async Task CreateMyAccountOnHostAsync()
    {
        var host = JoinAddress.Trim();
        var account = Launcher.Config.Account;
        if (host.Length == 0)
        {
            return;
        }

        await Main.RunAsync(async progress =>
        {
            progress.Report($"Creating account {account.Username} on {host}...");
            var reply = await Launcher.LoginClient.CreateAccountAsync(host, account.Username, account.Password);
            progress.Report(reply.Describe());
        });
    }
}
