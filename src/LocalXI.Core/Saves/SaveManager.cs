using System.IO.Compression;
using System.Text.Json;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Server;
using LocalXI.Core.Util;

namespace LocalXI.Core.Saves;

public sealed record SavedCharacter(string Name, string Account, string Job, int Level);

public sealed record SaveManifest(
    int Format,
    DateTime CreatedUtc,
    string Kind,
    string AppVersion,
    string? LsbCommit,
    string? ClientVersion,
    string? PrimaryAccount,
    IReadOnlyList<string> Tables,
    IReadOnlyList<SavedCharacter> Characters);

public sealed record SaveFileInfo(string Path, DateTime Modified, long SizeBytes, SaveManifest? Manifest)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// A save (.xisave) is a zip holding manifest.json and player-data.sql: a dump of every player table, schema
/// included, so it carries all characters, inventories, quests, missions and story progress to another PC.
/// </summary>
public sealed class SaveManager(AppPaths paths, GameDatabase database, MariaDbServer mariaDb, ServerPackage package)
{
    public const string Extension = ".xisave";
    public const int CurrentFormat = 1;

    private const string ManifestEntry = "manifest.json";
    private const string DataEntry = "player-data.sql";
    private const string AutoPrefix = "auto-";

    public async Task<SaveManifest> ExportAsync(string destination, string kind, string? primaryAccount, CancellationToken cancellationToken = default)
    {
        var tables = (await database.ExistingTablesAsync(package.PlayerTables, cancellationToken)).Order(StringComparer.Ordinal).ToList();
        var characters = (await new AccountStore(database).ListCharactersAsync(cancellationToken))
            .Select(c => new SavedCharacter(c.Name, c.Account, Jobs.Abbreviation(c.MainJob), c.MainLevel))
            .ToList();

        var manifest = new SaveManifest(
            CurrentFormat,
            DateTime.UtcNow,
            kind,
            typeof(SaveManager).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            package.LsbCommit,
            package.ClientVersion,
            primaryAccount,
            tables,
            characters);

        var work = Directory.CreateTempSubdirectory("localxi-save-");
        try
        {
            var sqlFile = Path.Combine(work.FullName, DataEntry);
            await database.DumpAsync(mariaDb.Tool("mariadb-dump"), tables, sqlFile, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(work.FullName, ManifestEntry), JsonSerializer.Serialize(manifest, JsonDefaults.Options), cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            var partial = destination + ".part";
            File.Delete(partial);
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(work.FullName, ManifestEntry), ManifestEntry);
                zip.CreateEntryFromFile(sqlFile, DataEntry, CompressionLevel.Optimal);
            }

            File.Move(partial, destination, overwrite: true);
            return manifest;
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>Writes an automatic backup to the Backups folder and prunes old automatic ones.</summary>
    public async Task<string> BackupAsync(string reason, string? primaryAccount, int keep, CancellationToken cancellationToken = default)
    {
        var prefix = reason == "auto" ? AutoPrefix : reason + "-";
        var file = Path.Combine(paths.BackupsDir, $"{prefix}{DateTime.Now:yyyyMMdd-HHmmss}{Extension}");
        await ExportAsync(file, "backup", primaryAccount, cancellationToken);
        PruneAutomaticBackups(keep);
        return file;
    }

    public void PruneAutomaticBackups(int keep)
    {
        foreach (var old in new DirectoryInfo(paths.BackupsDir)
                     .EnumerateFiles(AutoPrefix + "*" + Extension)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Skip(Math.Max(keep, 1)))
        {
            old.Delete();
        }
    }

    /// <summary>
    /// Replaces every player table with the save's contents. The game server must be stopped; the database running.
    /// Player tables the save doesn't know about (added by a newer server) are emptied so no stray rows attach
    /// to the imported characters.
    /// </summary>
    public async Task<SaveManifest> ImportAsync(string saveFile, CancellationToken cancellationToken = default)
    {
        using var zip = ZipFile.OpenRead(saveFile);
        var manifest = ReadManifest(zip) ?? throw new InvalidDataException("This file is not a LocalXI save (manifest.json is missing).");
        if (manifest.Format > CurrentFormat)
        {
            throw new InvalidDataException("This save was made by a newer version of LocalXI. Update the launcher and try again.");
        }

        var dataEntry = zip.GetEntry(DataEntry) ?? throw new InvalidDataException("This save has no character data in it.");
        string sql;
        using (var reader = new StreamReader(dataEntry.Open()))
        {
            sql = await reader.ReadToEndAsync(cancellationToken);
        }

        var saved = manifest.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toEmpty = (await database.ExistingTablesAsync(package.PlayerTables, cancellationToken)).Where(t => !saved.Contains(t)).ToList();

        var script = "SET foreign_key_checks=0;\n" +
                     string.Concat(toEmpty.Select(t => $"TRUNCATE TABLE `{t}`;\n")) +
                     sql +
                     "\nSET foreign_key_checks=1;\n";
        var result = await database.RunScriptAsync(mariaDb.Tool("mariadb"), script, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException("Loading the save failed:\n" + result.CombinedOutput);
        }

        return manifest;
    }

    public IReadOnlyList<SaveFileInfo> List(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return new DirectoryInfo(directory)
            .EnumerateFiles("*" + Extension)
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new SaveFileInfo(f.FullName, f.LastWriteTime, f.Length, TryReadManifest(f.FullName)))
            .ToList();
    }

    public static SaveManifest? TryReadManifest(string saveFile)
    {
        try
        {
            using var zip = ZipFile.OpenRead(saveFile);
            return ReadManifest(zip);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SaveManifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestEntry);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<SaveManifest>(stream, JsonDefaults.Options);
    }
}
