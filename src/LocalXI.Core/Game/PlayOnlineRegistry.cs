using Microsoft.Win32;

namespace LocalXI.Core.Game;

public sealed record PlayOnlineRegistryEntries(string? FfxiPath, string? ViewerPath);

/// <summary>
/// How Windows finds the game. xiloader creates PlayOnline (polcore.dll) and the game (FFXi.dll, which loads
/// FFXiMain.dll) as COM objects, so Windows looks their DLLs up in the class registrations under
/// HKLM\SOFTWARE\Classes\CLSID (32-bit view). xiloader then hands PlayOnline the folder in
/// HKLM\SOFTWARE\PlayOnlineUS\InstallFolder "1000" (PlayOnline Viewer), and the game reads its data from "0001"
/// (FINAL FANTASY XI). The official installer writes all of these; a folder copied from another PC has none of
/// them, which is the most common reason the game refuses to start.
/// </summary>
public static class PlayOnlineRegistry
{
    private const string InstallFolderKey = @"SOFTWARE\PlayOnlineUS\InstallFolder";

    // Machine-wide classes only: COM ignores per-user registrations in elevated processes, and the game runs elevated.
    private const string ClassesKey = @"SOFTWARE\Classes";

    // Windows Installer "advertised" registrations keep a descriptor in this value, which COM resolves through the
    // installer in preference to the plain path.
    private const string InstallerDescriptorValue = "InprocServer32";

    private enum Folder { Game, Viewer }

    private sealed record ComClass(string Clsid, string Name, Folder Folder, string File, string? ThreadingModel = null, string? ProgId = null, string? TypeLib = null);

    /// <summary>
    /// The classes the official US installer registers for files in the two folders, as read from a working
    /// install (October 2026 client). The PlayOnline movie source is also a DirectShow filter; its filter-category
    /// entries aren't needed to create it, so they aren't written.
    /// </summary>
    private static readonly ComClass[] ComClasses =
    [
        new("{989D790D-6236-11D4-80E9-00105A81E890}", "FFXiEntry Class", Folder.Game, "FFXi.dll", null, "FFXi.FFXiEntry", "{989D7900-6236-11D4-80E9-00105A81E890}"),
        new("{0DF0E951-D03C-4A94-90EF-40AE60668F5F}", "FxFileManager Class", Folder.Game, "FFXi.dll", null, "FFXi.FxFileManager", "{989D7900-6236-11D4-80E9-00105A81E890}"),
        new("{1027DC46-750D-4B1F-8834-1D25B8BEBAB8}", "GameMain Class", Folder.Game, "FFXiMain.dll", null, "FFXiMain.GameMain", "{4CFFC456-9368-4CA4-9435-B65E7A5674ED}"),
        new("{E636B693-A5A7-4161-9F02-D769178DE989}", "FFXiVersion Class", Folder.Game, "FFXiVersions.dll", null, "FFXiVersions.FFXiVersion", "{75AA93EE-48B1-4A54-B570-6E717178C49F}"),
        new("{3501F5DD-7894-42DF-866A-A2B6527D8049}", "POLCoreCom Class", Folder.Viewer, @"viewer\com\polcore.dll", "Apartment", "POLCore.POLCoreCom", "{3B0B8E16-C984-4792-ADD7-C23C75127DFD}"),
        new("{40555AAE-53AD-4ABC-AE65-8441755E7D69}", "PolAppCom Class", Folder.Viewer, @"viewer\com\app.dll", "Apartment", "App.PolAppCom", "{31F10EC9-53B2-40C6-84F1-CD703E05AA93}"),
        new("{62021866-976B-49A3-A18B-7A44869008A2}", "PolContentsCom Class", Folder.Viewer, @"viewer\contents\PolContents.dll", "Apartment", "PolContents.PolContentsCom", "{E08373D9-A0A9-480F-AC57-403BFE1044AD}"),
        new("{3FC1EF9A-F346-413C-BB47-ED6F9A4BD52F}", "PolContentsCom Class", Folder.Viewer, @"viewer\contents\polcontentsINT.dll", "Apartment", "PolContents.PolContentsCom", "{945A80FB-5FE3-4FAA-8C31-43685D70C9C1}"),
        new("{E697F40A-B701-4F75-A201-635FA69A41B3}", "PlayOnline Movie File Source", Folder.Viewer, @"viewer\ax\polmvf.dll", "Both"),
        new("{52031487-AE6C-450E-91FD-87E5112C0FEE}", "PlayOnline Movie File Source", Folder.Viewer, @"viewer\ax\polmvfINT.dll", "Both"),
    ];

    public static PlayOnlineRegistryEntries? Read()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = hklm.OpenSubKey(InstallFolderKey);
        return key is null ? null : new PlayOnlineRegistryEntries(key.GetValue("0001") as string, key.GetValue("1000") as string);
    }

    /// <summary>True when both install folders and every class with a DLL in them point at this install.</summary>
    public static bool Matches(FfxiInstallation install)
    {
        var entries = Read();
        if (entries is null || !SamePath(entries.FfxiPath, install.FfxiPath))
        {
            return false;
        }

        if (install.PlayOnlineViewerPath is null)
        {
            return true;
        }

        if (!SamePath(entries.ViewerPath, install.PlayOnlineViewerPath))
        {
            return false;
        }

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var classes = hklm.OpenSubKey(ClassesKey);
        return classes is not null && ComClassesMatch(classes, install.FfxiPath, install.PlayOnlineViewerPath);
    }

    /// <summary>
    /// Writes the install folders and the class registrations. Requires administrator rights, so the launcher runs
    /// this in an elevated copy of itself.
    /// </summary>
    public static void Write(string ffxiPath, string viewerPath)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using (var key = hklm.CreateSubKey(InstallFolderKey, writable: true))
        {
            // The official installer stores the game folder with a trailing backslash; keep that shape.
            key.SetValue("0001", ffxiPath.TrimEnd('\\') + "\\", RegistryValueKind.String);
            key.SetValue("1000", viewerPath.TrimEnd('\\'), RegistryValueKind.String);
        }

        using var classes = hklm.CreateSubKey(ClassesKey, writable: true);
        WriteComClasses(classes, ffxiPath, viewerPath);
    }

    internal static bool ComClassesMatch(RegistryKey classes, string ffxiPath, string viewerPath) =>
        ExpectedComClasses(ffxiPath, viewerPath).All(e => SamePath(RegisteredPath(classes, e.Class.Clsid), e.Path));

    /// <summary>
    /// Registers each class whose DLL is in the folders, as the official installer does, skipping classes already
    /// registered there so a working install is left alone. Returns how many were written.
    /// </summary>
    internal static int WriteComClasses(RegistryKey classes, string ffxiPath, string viewerPath)
    {
        var written = 0;
        foreach (var (comClass, path) in ExpectedComClasses(ffxiPath, viewerPath))
        {
            if (SamePath(RegisteredPath(classes, comClass.Clsid), path))
            {
                continue;
            }

            using var key = classes.CreateSubKey($@"CLSID\{comClass.Clsid}", writable: true);
            key.SetValue("", comClass.Name);
            using (var server = key.CreateSubKey("InprocServer32", writable: true))
            {
                server.SetValue("", path);
                server.DeleteValue(InstallerDescriptorValue, throwOnMissingValue: false);
                if (comClass.ThreadingModel is not null)
                {
                    server.SetValue("ThreadingModel", comClass.ThreadingModel);
                }
            }

            if (comClass.ProgId is not null)
            {
                SetDefault(key, "ProgID", comClass.ProgId + ".1");
                SetDefault(key, "VersionIndependentProgID", comClass.ProgId);
            }

            if (comClass.TypeLib is not null)
            {
                SetDefault(key, "TypeLib", comClass.TypeLib);
            }

            written++;
        }

        return written;
    }

    private static IEnumerable<(ComClass Class, string Path)> ExpectedComClasses(string ffxiPath, string viewerPath) =>
        ComClasses
            .Select(c => (Class: c, Path: Path.Combine(c.Folder == Folder.Game ? ffxiPath : viewerPath, c.File)))
            .Where(e => File.Exists(e.Path));

    private static string? RegisteredPath(RegistryKey classes, string clsid)
    {
        using var server = classes.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
        return (server?.GetValue("") as string)?.Trim().Trim('"');
    }

    private static void SetDefault(RegistryKey parent, string subKey, string value)
    {
        using var key = parent.CreateSubKey(subKey, writable: true);
        key.SetValue("", value);
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.GetFullPath(a.Trim()).TrimEnd('\\'), Path.GetFullPath(b.Trim()).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
