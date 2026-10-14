namespace LocalXI.Core;

/// <summary>Every folder the launcher reads or writes, resolved once at startup.</summary>
public sealed class AppPaths
{
    public AppPaths(string dataRoot, string userRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        UserRoot = Path.GetFullPath(userRoot);
    }

    /// <summary>Machine-local state: downloaded components, the live database, logs and config.</summary>
    public string DataRoot { get; }

    /// <summary>Files the player cares about and may carry to another PC: exported saves and backups.</summary>
    public string UserRoot { get; }

    public string ConfigFile => Path.Combine(DataRoot, "config.json");
    public string Downloads => Path.Combine(DataRoot, "downloads");
    public string Runtime => Path.Combine(DataRoot, "runtime");
    public string ComponentMarkers => Path.Combine(Runtime, ".components");
    public string MariaDbDir => Path.Combine(Runtime, "mariadb");
    public string ServerDir => Path.Combine(Runtime, "server");
    public string AshitaDir => Path.Combine(Runtime, "ashita");
    public string DatabaseDir => Path.Combine(DataRoot, "database");
    public string LogsDir => Path.Combine(DataRoot, "logs");
    public string SavesDir => Path.Combine(UserRoot, "Saves");
    public string BackupsDir => Path.Combine(UserRoot, "Backups");
    public string ScreenshotsDir => Path.Combine(UserRoot, "Screenshots");

    /// <summary>
    /// Normal installs keep machine state in %LOCALAPPDATA%\LocalXI and saves in Documents\LocalXI.
    /// Dropping a <c>portable.txt</c> next to the exe keeps everything beside it instead (USB stick installs).
    /// </summary>
    public static AppPaths Resolve()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            var root = Path.Combine(exeDir, "LocalXI-data");
            return new AppPaths(root, Path.Combine(root, "user"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppPaths(Path.Combine(local, "LocalXI"), Path.Combine(documents, "LocalXI"));
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataRoot, Downloads, Runtime, ComponentMarkers, LogsDir, SavesDir, BackupsDir, ScreenshotsDir })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
