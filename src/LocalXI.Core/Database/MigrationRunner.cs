using LocalXI.Core.Util;

namespace LocalXI.Core.Database;

/// <summary>What one run of LandSandBoat's migrations did.</summary>
/// <param name="Ran">Migrations that were needed and now are not ("NNN_name: description").</param>
/// <param name="Failed">Migrations still needed after running, with the error if there was one.</param>
/// <param name="Warnings">Problems that don't stop the world from working, such as one character's unreadable data.</param>
public sealed record MigrationResult(IReadOnlyList<string> Ran, IReadOnlyList<string> Failed, IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Failed.Count == 0;
}

/// <summary>
/// Converts the player tables of an older LandSandBoat version to the installed one, by running LandSandBoat's own
/// migrations (server/tools/migrations) the way its tools/dbtool.py does: ask every migration whether it's needed,
/// then run the needed ones in file-name order. dbtool itself needs a full Python setup with many packages and asks
/// questions, so LocalXI runs the migrations through a small driver on a private, pinned Python with only the
/// MariaDB connector (the "python" and "python-mariadb" components). Each migration checks the tables itself, so
/// running them on an up-to-date world changes nothing.
/// </summary>
public sealed class MigrationRunner(AppPaths paths, GameDatabase database, string serverDirectory)
{
    private const string DriverName = "localxi_migrate.py";

    private string Python => Path.Combine(paths.Runtime, "python", "python.exe");
    private string Tools => Path.Combine(serverDirectory, "tools");

    // The MariaDB connector and the one package it needs, each a component in its own folder.
    private string[] Packages => [Path.Combine(paths.Runtime, "python-packages"), Path.Combine(paths.Runtime, "python-packaging")];

    /// <summary>
    /// Mirrors run_all_migrations in dbtool.py, minus its prompts and colours. Reports one tab-separated line per
    /// event so the launcher can read the outcome: NEEDED, RUN, ERROR, STILL, DONE.
    /// </summary>
    private const string Driver = """
        # Written by LocalXI each time it converts character data; changes here are overwritten.
        # Runs LandSandBoat's migrations like tools/dbtool.py: check every one, then run the needed ones in order.
        import importlib, os, sys

        tools_dir = sys.argv[1]
        sys.path[:0] = sys.argv[2:] + [tools_dir]
        import mariadb

        def report(kind, name="", text=""):
            print("\t".join([kind, name, str(text).replace("\t", " ").replace("\r", " ").replace("\n", " ")]), flush=True)

        db = mariadb.connect(host=os.environ["LOCALXI_DB_HOST"], port=int(os.environ["LOCALXI_DB_PORT"]),
                             user=os.environ["LOCALXI_DB_USER"], passwd=os.environ["LOCALXI_DB_PASSWORD"],
                             db=os.environ["LOCALXI_DB_NAME"])
        cur = db.cursor()

        names = sorted(f[:-3] for f in os.listdir(os.path.join(tools_dir, "migrations")) if f.endswith(".py"))
        needed = []
        for name in names:
            try:
                module = importlib.import_module("migrations." + name)
                module.check_preconditions(cur)
                if module.needs_to_run(cur):
                    needed.append((name, module))
                    report("NEEDED", name, module.migration_name())
            except Exception as e:
                report("ERROR", name, repr(e))

        for name, module in needed:
            report("RUN", name, module.migration_name())
            try:
                module.migrate(cur, db)
                db.commit()
            except Exception as e:
                report("ERROR", name, repr(e))
            try:
                if module.needs_to_run(cur):
                    report("STILL", name, module.migration_name())
            except Exception as e:
                report("STILL", name, repr(e))

        report("DONE")

        """;

    /// <summary>True once the Python components are installed and the server ships migrations.</summary>
    public bool IsAvailable =>
        File.Exists(Python) && Directory.Exists(Path.Combine(Packages[0], "mariadb")) && Directory.Exists(Path.Combine(Packages[1], "packaging")) &&
        Directory.Exists(Path.Combine(Tools, "migrations"));

    public async Task<MigrationResult> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("The character data converter isn't installed. Press Play to finish the update, then try again.");
        }

        var driver = Path.Combine(paths.Runtime, DriverName);
        await File.WriteAllTextAsync(driver, Driver.ReplaceLineEndings("\n"), cancellationToken);

        // Some migrations append problems with single characters to migration_errors.log in the working directory.
        Directory.CreateDirectory(paths.LogsDir);
        var errorLog = Path.Combine(paths.LogsDir, "migration_errors.log");
        File.Delete(errorLog);

        progress?.Report("Checking whether your character data needs converting...");
        var settings = database.Settings;
        var result = await ProcessRunner.RunAsync(
            Python,
            // -I isolated (no user site or environment), -B no bytecode files in the server folder, -X utf8 text;
            // the connector's docstrings trip a harmless SyntaxWarning on newer Pythons.
            ["-I", "-B", "-X", "utf8", "-W", "ignore::SyntaxWarning", driver, Tools, .. Packages],
            workingDirectory: paths.LogsDir,
            environment: new Dictionary<string, string>
            {
                ["LOCALXI_DB_HOST"] = "127.0.0.1",
                ["LOCALXI_DB_PORT"] = settings.Port.ToString(),
                ["LOCALXI_DB_USER"] = "root",
                ["LOCALXI_DB_PASSWORD"] = settings.RootPassword,
                ["LOCALXI_DB_NAME"] = settings.Name,
            },
            cancellationToken: cancellationToken);

        foreach (var line in Lines(result.StdOut).Where(l => l.StartsWith("RUN\t", StringComparison.Ordinal)))
        {
            progress?.Report($"Converting character data: {line.Split('\t').ElementAtOrDefault(2)}...");
        }

        var errorLogText = File.Exists(errorLog) ? await File.ReadAllTextAsync(errorLog, cancellationToken) : null;
        return ParseReport(result.StdOut, result.Success, result.CombinedOutput, errorLogText);
    }

    /// <summary>Reads the driver's report (see <see cref="Driver"/>).</summary>
    internal static MigrationResult ParseReport(string report, bool exitedCleanly, string output, string? errorLog)
    {
        var ran = new List<string>();
        var failed = new List<string>();
        var errors = new Dictionary<string, string>();
        var done = false;
        foreach (var line in Lines(report))
        {
            var parts = line.Split('\t');
            var (kind, name, text) = (parts[0], parts.ElementAtOrDefault(1) ?? "", parts.ElementAtOrDefault(2) ?? "");
            switch (kind)
            {
                case "RUN":
                    ran.Add($"{name}: {text}");
                    break;
                case "ERROR":
                    errors[name] = text;
                    break;
                case "STILL":
                    failed.Add(errors.TryGetValue(name, out var error) ? $"{name}: {text} ({error})" : $"{name}: {text}");
                    ran.RemoveAll(r => r.StartsWith(name + ":", StringComparison.Ordinal));
                    break;
                case "DONE":
                    done = true;
                    break;
            }
        }

        // A migration that failed while being checked never reaches RUN; count it as failed too.
        failed.AddRange(errors.Where(e => !failed.Any(f => f.StartsWith(e.Key + ":", StringComparison.Ordinal)) && !ran.Any(r => r.StartsWith(e.Key + ":", StringComparison.Ordinal)))
            .Select(e => $"{e.Key}: {e.Value}"));

        if (!done || !exitedCleanly)
        {
            failed.Add("The converter stopped early: " + LastLines(output));
        }

        // dbtool only warns about these too: the migration ran, but skipped some characters' malformed data.
        var warnings = errorLog is null ? [] : new List<string> { "Some characters had data the converter couldn't read: " + LastLines(errorLog) };
        return new MigrationResult(ran, failed, warnings);
    }

    private static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string LastLines(string text) => string.Join(" / ", Lines(text).TakeLast(4));
}
