using LocalXI.Core.Game;
using Microsoft.Win32;

namespace LocalXI.Core.Tests;

/// <summary>
/// Class registration for copied installs, written into a throwaway per-user key instead of the machine's classes.
/// </summary>
public sealed class PlayOnlineRegistryTests : IDisposable
{
    private const string FFXiEntry = "{989D790D-6236-11D4-80E9-00105A81E890}";
    private const string PolCore = "{3501F5DD-7894-42DF-866A-A2B6527D8049}";
    private const string FFXiVersion = "{E636B693-A5A7-4161-9F02-D769178DE989}";

    private readonly string _keyPath = $@"Software\LocalXI-Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _classes;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "localxi-reg-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _viewer;

    public PlayOnlineRegistryTests()
    {
        _classes = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        _game = Path.Combine(_root, "FINAL FANTASY XI");
        _viewer = Path.Combine(_root, "PlayOnlineViewer");

        // A copied install with some of the files: FFXiVersions.dll and the movie source are missing on purpose.
        foreach (var file in new[]
                 {
                     Path.Combine(_game, "FFXi.dll"), Path.Combine(_game, "FFXiMain.dll"),
                     Path.Combine(_viewer, "viewer", "com", "polcore.dll"), Path.Combine(_viewer, "viewer", "com", "app.dll"),
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, []);
        }
    }

    public void Dispose()
    {
        _classes.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\LocalXI-Tests"))
        {
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
            {
                parent.Close();
                Registry.CurrentUser.DeleteSubKey(@"Software\LocalXI-Tests", throwOnMissingSubKey: false);
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void WriteComClasses_RegistersClassesWhoseFilesExist()
    {
        Assert.False(PlayOnlineRegistry.ComClassesMatch(_classes, _game, _viewer));

        // FFXiEntry + FxFileManager (FFXi.dll), GameMain (FFXiMain.dll), POLCoreCom (polcore.dll), PolAppCom (app.dll)
        Assert.Equal(5, PlayOnlineRegistry.WriteComClasses(_classes, _game, _viewer));
        Assert.True(PlayOnlineRegistry.ComClassesMatch(_classes, _game, _viewer));

        using (var entry = _classes.OpenSubKey($@"CLSID\{FFXiEntry}")!)
        {
            Assert.Equal("FFXiEntry Class", entry.GetValue(""));
            Assert.Equal(Path.Combine(_game, "FFXi.dll"), entry.OpenSubKey("InprocServer32")!.GetValue(""));
            Assert.Null(entry.OpenSubKey("InprocServer32")!.GetValue("ThreadingModel"));
            Assert.Equal("FFXi.FFXiEntry.1", entry.OpenSubKey("ProgID")!.GetValue(""));
        }

        using (var polcore = _classes.OpenSubKey($@"CLSID\{PolCore}\InprocServer32")!)
        {
            Assert.Equal(Path.Combine(_viewer, "viewer", "com", "polcore.dll"), polcore.GetValue(""));
            Assert.Equal("Apartment", polcore.GetValue("ThreadingModel"));
        }

        Assert.Null(_classes.OpenSubKey($@"CLSID\{FFXiVersion}"));
        Assert.Equal(0, PlayOnlineRegistry.WriteComClasses(_classes, _game, _viewer));
    }

    [Fact]
    public void WriteComClasses_RepointsAMovedInstallAndDropsTheInstallerDescriptor()
    {
        using (var old = _classes.CreateSubKey($@"CLSID\{PolCore}\InprocServer32", writable: true))
        {
            old.SetValue("", @"D:\Old\PlayOnlineViewer\viewer\com\polcore.dll");
            old.SetValue("InprocServer32", new[] { "advertised-descriptor" }, RegistryValueKind.MultiString);
            old.SetValue("ThreadingModel", "Apartment");
        }

        PlayOnlineRegistry.WriteComClasses(_classes, _game, _viewer);

        using var server = _classes.OpenSubKey($@"CLSID\{PolCore}\InprocServer32")!;
        Assert.Equal(Path.Combine(_viewer, "viewer", "com", "polcore.dll"), server.GetValue(""));
        Assert.Null(server.GetValue("InprocServer32"));
    }

    [Fact]
    public void WriteComClasses_LeavesCorrectRegistrationsAlone()
    {
        var path = Path.Combine(_viewer, "viewer", "com", "polcore.dll");
        using (var server = _classes.CreateSubKey($@"CLSID\{PolCore}\InprocServer32", writable: true))
        {
            server.SetValue("", $"\"{path.ToUpperInvariant()}\"");
            server.SetValue("InprocServer32", new[] { "advertised-descriptor" }, RegistryValueKind.MultiString);
        }

        Assert.Equal(4, PlayOnlineRegistry.WriteComClasses(_classes, _game, _viewer));

        using var after = _classes.OpenSubKey($@"CLSID\{PolCore}\InprocServer32")!;
        Assert.Equal($"\"{path.ToUpperInvariant()}\"", after.GetValue(""));
        Assert.NotNull(after.GetValue("InprocServer32"));
    }
}
