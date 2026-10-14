using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalXI.Core.Game;
using LocalXI.Core.Network;

namespace LocalXI.Core;

/// <summary>
/// The launcher never runs as administrator. The few steps that need it (registry repair, firewall rules) run in a
/// second copy of the launcher started with a UAC prompt, which does that one task and exits.
/// </summary>
public static class Elevation
{
    public const string Flag = "--elevated";

    public const string RepairRegistry = "repair-registry";
    public const string AddFirewallRules = "firewall-add";
    public const string RemoveFirewallRules = "firewall-remove";

    /// <summary>Runs a task elevated. Returns false if the user declined the UAC prompt; throws if the task failed.</summary>
    public static async Task<bool> RunAsync(string task, params string[] arguments)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the launcher's own executable.");
        var resultFile = Path.Combine(Path.GetTempPath(), $"localxi-elevated-{Guid.NewGuid():N}.txt");

        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', new[] { Flag, resultFile, task }.Concat(arguments).Select(QuoteArgument)),
        };

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the elevated helper.");
            await process.WaitForExitAsync();
            var message = File.Exists(resultFile) ? (await File.ReadAllTextAsync(resultFile)).Trim() : "";
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(message.Length > 0 ? message : $"The administrator task '{task}' failed (exit code {process.ExitCode}).");
            }

            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return false; // ERROR_CANCELLED: the user chose "No" on the UAC prompt.
        }
        finally
        {
            File.Delete(resultFile);
        }
    }

    /// <summary>Entry point inside the elevated copy: <c>--elevated &lt;resultFile&gt; &lt;task&gt; [args...]</c>.</summary>
    public static async Task<int> ExecuteAsync(IReadOnlyList<string> args)
    {
        if (args.Count < 3 || args[0] != Flag)
        {
            return 2;
        }

        var resultFile = args[1];
        try
        {
            switch (args[2])
            {
                case RepairRegistry:
                    PlayOnlineRegistry.Write(args[3], args[4]);
                    break;
                case AddFirewallRules:
                    await FirewallRules.InstallAsync(args[3]);
                    break;
                case RemoveFirewallRules:
                    await FirewallRules.RemoveAsync();
                    break;
                default:
                    throw new ArgumentException($"Unknown task '{args[2]}'.");
            }

            await File.WriteAllTextAsync(resultFile, "ok");
            return 0;
        }
        catch (Exception e)
        {
            await File.WriteAllTextAsync(resultFile, e.Message);
            return 1;
        }
    }

    /// <summary>Quotes one argument using the rules CommandLineToArgvW expects.</summary>
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            builder.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            builder.Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}
