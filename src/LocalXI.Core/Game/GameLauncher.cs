using System.Diagnostics;
using LocalXI.Core.Config;
using LocalXI.Core.Util;

namespace LocalXI.Core.Game;

/// <summary>Starts FINAL FANTASY XI through Ashita, with xiloader as the bootloader pointed at the chosen server.</summary>
public sealed class GameLauncher(AppPaths paths)
{
    public const string BootConfigName = "localxi.ini";

    // With xiloader as Ashita's bootloader the game runs inside the xiloader process.
    private const string GameProcessName = "xiloader";

    public string AshitaCli => Path.Combine(paths.AshitaDir, "Ashita-cli.exe");
    public string BootConfigPath => Path.Combine(paths.AshitaDir, "config", "boot", BootConfigName);

    public bool IsGameRunning => CountGameProcesses() > 0;

    public void WriteBootConfig(string serverAddress, AccountSettings account, GameWindowSettings window)
    {
        var bootDir = Path.GetDirectoryName(BootConfigPath)!;
        Directory.CreateDirectory(bootDir);

        // Start from Ashita's own private-server example so its defaults (fonts, input, D3D) stay upstream's.
        var template = Path.Combine(bootDir, "example-privateserver.ini");
        var ini = File.Exists(template) ? IniFile.Load(template) : new IniFile([]);

        ini.Set("ashita.launcher", "autoclose", "1");
        ini.Set("ashita.launcher", "name", "LocalXI");

        ini.Set("ashita.boot", "file", @".\\bootloader\\xiloader.exe");
        ini.Set("ashita.boot", "command", XiloaderCommand(serverAddress, account));
        ini.Set("ashita.boot", "gamemodule", "ffximain.dll");
        ini.Set("ashita.boot", "script", "default.txt");
        ini.Set("ashita.boot", "args", "");

        // Game settings normally written by FINAL FANTASY XI's config tool; Ashita applies them at launch.
        ini.Set("ffxi.registry", "0001", window.Width.ToString());
        ini.Set("ffxi.registry", "0002", window.Height.ToString());
        ini.Set("ffxi.registry", "0037", window.Width.ToString());
        ini.Set("ffxi.registry", "0038", window.Height.ToString());
        ini.Set("ffxi.registry", "0034", ((int)window.Mode).ToString());
        Directory.CreateDirectory(paths.ScreenshotsDir);
        ini.Set("ffxi.registry", "0042", paths.ScreenshotsDir);

        ini.Save(BootConfigPath);
    }

    internal static string XiloaderCommand(string serverAddress, AccountSettings account) =>
        $"--server {serverAddress} --user {account.Username} --pass {account.Password} --hide";

    public void Launch()
    {
        if (!File.Exists(AshitaCli))
        {
            throw new FileNotFoundException("Ashita is not installed. Run setup again from Settings.", AshitaCli);
        }

        // Ashita-cli.exe's manifest requires administrator rights (it injects into the game). Starting it directly
        // fails with "requires elevation"; going through the shell makes Windows show its permission prompt instead.
        var startInfo = new ProcessStartInfo(AshitaCli)
        {
            WorkingDirectory = paths.AshitaDir,
            UseShellExecute = true,
            Arguments = BootConfigName,
        };

        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException(
                "The game wasn't started because the Windows permission prompt was declined. " +
                "Ashita needs administrator rights to start FINAL FANTASY XI.");
        }
    }

    /// <summary>Waits for the game to appear (Ashita takes a moment) and then to close.</summary>
    public async Task WaitForGameToCloseAsync(CancellationToken cancellationToken)
    {
        var appearDeadline = Stopwatch.StartNew();
        while (!IsGameRunning)
        {
            if (appearDeadline.Elapsed > TimeSpan.FromSeconds(60))
            {
                return; // Never started (closed at the loader, or failed); nothing to wait for.
            }

            await Task.Delay(1000, cancellationToken);
        }

        while (IsGameRunning)
        {
            await Task.Delay(2000, cancellationToken);
        }
    }

    private int CountGameProcesses()
    {
        var bootloaderDir = Path.Combine(paths.AshitaDir, "bootloader");
        var count = 0;
        foreach (var process in Process.GetProcessesByName(GameProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName.StartsWith(bootloaderDir, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        count++;
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A game run as administrator can't be inspected from here; count it as ours.
                    count++;
                }
            }
        }

        return count;
    }
}
