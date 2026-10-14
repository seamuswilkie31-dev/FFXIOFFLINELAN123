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

    /// <summary>
    /// The .sql files of LandSandBoat's era data packs (modules/era/sql/&lt;tag&gt;), in the order dbtool would apply
    /// them. Packs a server version doesn't have are skipped.
    /// </summary>
    public IReadOnlyList<string> EraSqlFiles(IEnumerable<string> packs) =>
        packs
            .Select(tag => Path.Combine(serverDir, "modules", "era", "sql", tag))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.sql", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            .ToList();

    /// <summary>Fresh install: import everything.</summary>
    public Task ImportAllAsync(IProgress<ImportProgress>? progress, CancellationToken cancellationToken) =>
        ImportFilesAsync(SqlFiles(), progress, cancellationToken);

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
        await ImportFilesAsync(files, progress, cancellationToken);
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> files, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
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
        var result = await RunFileAsync(file, continueOnError: false, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Importing {Path.GetFileName(file)} failed:\n{result.CombinedOutput}");
        }
    }

    /// <summary>
    /// Applies era data packs, skipping individual statements that fail. The packs are optional extras that can lag
    /// behind schema changes upstream, so (like LandSandBoat's dbtool) a stale line shouldn't block the whole world.
    /// Returns a description of anything skipped.
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplyEraPacksAsync(IReadOnlyList<string> files, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
    {
        var skipped = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            progress?.Report(new ImportProgress(i, files.Count, Path.GetFileName(files[i])));
            var result = await RunFileAsync(files[i], continueOnError: true, cancellationToken);
            // With --force the client can exit 0 even though statements failed; the errors are on stderr.
            if (!result.Success || result.StdErr.Contains("ERROR", StringComparison.Ordinal))
            {
                skipped.Add($"{Path.GetRelativePath(serverDir, files[i])}: {result.CombinedOutput}");
            }
        }

        progress?.Report(new ImportProgress(files.Count, files.Count, ""));
        return skipped;
    }

    private async Task<Util.ProcessResult> RunFileAsync(string file, bool continueOnError, CancellationToken cancellationToken)
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
        return await database.RunScriptAsync(mariaDbClient, script, cancellationToken, continueOnError);
    }
}
