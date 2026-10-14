using Microsoft.Win32;

namespace LocalXI.Core.Game;

public sealed record PlayOnlineRegistryEntries(string? FfxiPath, string? ViewerPath);

/// <summary>
/// The game and xiloader find their files through HKLM\SOFTWARE\PlayOnlineUS\InstallFolder (32-bit view):
/// value "0001" is the FINAL FANTASY XI folder and "1000" the PlayOnline Viewer folder. Installs copied from
/// another PC lack these entries, which is the most common reason the game refuses to start.
/// </summary>
public static class PlayOnlineRegistry
{
    private const string InstallFolderKey = @"SOFTWARE\PlayOnlineUS\InstallFolder";

    public static PlayOnlineRegistryEntries? Read()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = hklm.OpenSubKey(InstallFolderKey);
        return key is null ? null : new PlayOnlineRegistryEntries(key.GetValue("0001") as string, key.GetValue("1000") as string);
    }

    public static bool Matches(FfxiInstallation install)
    {
        var entries = Read();
        return entries is not null
            && SamePath(entries.FfxiPath, install.FfxiPath)
            && (install.PlayOnlineViewerPath is null || SamePath(entries.ViewerPath, install.PlayOnlineViewerPath));
    }

    /// <summary>Writes the entries. Requires administrator rights, so the launcher runs this in an elevated copy of itself.</summary>
    public static void Write(string ffxiPath, string viewerPath)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = hklm.CreateSubKey(InstallFolderKey, writable: true);
        // The official installer stores the game folder with a trailing backslash; keep that shape.
        key.SetValue("0001", ffxiPath.TrimEnd('\\') + "\\", RegistryValueKind.String);
        key.SetValue("1000", viewerPath.TrimEnd('\\'), RegistryValueKind.String);
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.GetFullPath(a.Trim()).TrimEnd('\\'), Path.GetFullPath(b.Trim()).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
