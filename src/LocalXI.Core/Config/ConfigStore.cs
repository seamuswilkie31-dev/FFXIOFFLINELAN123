using System.Text.Json;
using LocalXI.Core.Util;

namespace LocalXI.Core.Config;

public static class ConfigStore
{
    public static LauncherConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LauncherConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path), JsonDefaults.Options) ?? new LauncherConfig();
        }
        catch (JsonException)
        {
            // Keep the broken file for inspection rather than silently losing it, then start fresh.
            File.Copy(path, path + ".broken", overwrite: true);
            return new LauncherConfig();
        }
    }

    public static void Save(string path, LauncherConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonDefaults.Options));
        File.Move(temp, path, overwrite: true);
    }
}
