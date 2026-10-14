namespace LocalXI.Core.Database;

public sealed record ImportProgress(int Done, int Total, string File);

/// <summary>
/// Loads LandSandBoat's sql/*.sql files the same way its tools/dbtool.py does: alphabetical order, triggers last,
/// each file wrapped with checks disabled for speed.
/// </summary>
public sealed class SchemaImporter(GameDatabase database, string mariaDbClient, string serverDir)
{
    public IReadOnlyList<string> SqlFiles()
    {
        var files = Directory.GetFiles(Path.Combine(serverDir, "sql"), "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var triggers = files.FirstOrDefault(f => Path.GetFileName(f).Equals("triggers.sql", StringComparison.OrdinalIgnoreCase));
        if (triggers is not null)
        {
            files.Remove(triggers);
            files.Add(triggers);
        }

        return files;
    }

    /// <summary>Fresh install: import everything.</summary>
    public Task ImportAllAsync(IProgress<ImportProgress>? progress, CancellationToken cancellationToken) =>
        ImportAsync(SqlFiles(), progress, cancellationToken);

    /// <summary>
    /// After a server update: refresh all game data but never overwrite player tables (only create ones that are missing).
    /// Schema changes to player tables also need LandSandBoat's Python migrations, which are not automated yet.
    /// </summary>
    public async Task RefreshGameDataAsync(IReadOnlyCollection<string> playerTables, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = await database.ExistingTablesAsync(playerTables, cancellationToken);
        var protectedTables = playerTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = SqlFiles()
            .Where(f =>
            {
                var table = Path.GetFileNameWithoutExtension(f);
                return !protectedTables.Contains(table) || !existing.Contains(table);
            })
            .ToList();
        await ImportAsync(files, progress, cancellationToken);
    }

    private async Task ImportAsync(IReadOnlyList<string> files, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
    {
        for (var i = 0; i < files.Count; i++)
        {
            progress?.Report(new ImportProgress(i, files.Count, Path.GetFileName(files[i])));
            await ImportFileAsync(files[i], cancellationToken);
        }

        progress?.Report(new ImportProgress(files.Count, files.Count, ""));
    }

    public async Task ImportFileAsync(string file, CancellationToken cancellationToken)
    {
        var sql = await File.ReadAllTextAsync(file, cancellationToken);
        var script = $"""
            SET autocommit=0;
            SET unique_checks=0;
            SET foreign_key_checks=0;
            {sql}

            SET unique_checks=1;
            SET foreign_key_checks=1;
            COMMIT;

            """;
        var result = await database.RunScriptAsync(mariaDbClient, script, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Importing {Path.GetFileName(file)} failed:\n{result.CombinedOutput}");
        }
    }
}
