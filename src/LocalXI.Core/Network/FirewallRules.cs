using LocalXI.Core.Util;

namespace LocalXI.Core.Network;

/// <summary>
/// Windows Firewall rules that let friends reach a hosted server. Rules are per program (not per port) and only
/// accept traffic from the local network and the address ranges used by common gaming VPNs, so nothing is opened
/// to the wider internet even if a router forwards ports.
/// </summary>
public static class FirewallRules
{
    public const string RulePrefix = "LocalXI";

    /// <summary>LocalSubnet covers home LANs; 26/8 is Radmin VPN, 25/8 Hamachi, 100.64/10 Tailscale.</summary>
    public const string AllowedRemoteAddresses = "LocalSubnet,26.0.0.0/8,25.0.0.0/8,100.64.0.0/10";

    public static readonly string[] ServerPrograms = ["xi_connect", "xi_search", "xi_map", "xi_profile", "xi_world"];

    private static string RuleName(string program) => $"{RulePrefix} - {program}";

    /// <summary>Checking does not need administrator rights.</summary>
    public static async Task<bool> AreInstalledAsync(string serverDir)
    {
        foreach (var program in ServerPrograms.Where(p => File.Exists(Path.Combine(serverDir, p + ".exe"))))
        {
            var result = await ProcessRunner.RunAsync("netsh", ["advfirewall", "firewall", "show", "rule", $"name={RuleName(program)}", "verbose"]);
            if (!result.Success || !result.StdOut.Contains(Path.Combine(serverDir, program + ".exe"), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Requires administrator rights; the launcher runs this in an elevated copy of itself.</summary>
    public static async Task InstallAsync(string serverDir)
    {
        await RemoveAsync();
        foreach (var program in ServerPrograms)
        {
            var path = Path.Combine(serverDir, program + ".exe");
            if (!File.Exists(path))
            {
                continue;
            }

            var result = await ProcessRunner.RunAsync("netsh",
            [
                "advfirewall", "firewall", "add", "rule",
                $"name={RuleName(program)}",
                "dir=in",
                "action=allow",
                $"program={path}",
                "enable=yes",
                "profile=any",
                $"remoteip={AllowedRemoteAddresses}",
            ]);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Adding the firewall rule for {program} failed:\n{result.CombinedOutput}");
            }
        }
    }

    public static async Task RemoveAsync()
    {
        foreach (var program in ServerPrograms)
        {
            // Fails harmlessly when the rule doesn't exist.
            await ProcessRunner.RunAsync("netsh", ["advfirewall", "firewall", "delete", "rule", $"name={RuleName(program)}"]);
        }
    }
}
