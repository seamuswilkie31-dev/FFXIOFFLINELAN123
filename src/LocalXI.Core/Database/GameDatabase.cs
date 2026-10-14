using LocalXI.Core.Config;
using LocalXI.Core.Util;
using MySqlConnector;

namespace LocalXI.Core.Database;

/// <summary>Talks to the launcher's own MariaDB instance, either through MySqlConnector or the bundled command-line tools.</summary>
public sealed class GameDatabase(AppPaths paths, DatabaseSettings settings)
{
    public DatabaseSettings Settings => settings;

    private string ClientConfigFile => Path.Combine(paths.DataRoot, "mariadb-client.cnf");

    public string ConnectionString(bool includeDatabase = true) => new MySqlConnectionStringBuilder
    {
        Server = "127.0.0.1",
        Port = (uint)settings.Port,
        UserID = "root",
        Password = settings.RootPassword,
        Database = includeDatabase ? settings.Name : "",
        ConnectionTimeout = 5,
        CharacterSet = "utf8mb4",
        Pooling = false,
    }.ConnectionString;

    public async Task<MySqlConnection> OpenAsync(bool includeDatabase = true, CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(ConnectionString(includeDatabase));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(includeDatabase: false, cancellationToken);
            return true;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    public async Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(includeDatabase: false, cancellationToken);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name", connection);
        command.Parameters.AddWithValue("@name", settings.Name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(includeDatabase: false, cancellationToken);
        await using var command = new MySqlCommand(
            $"CREATE DATABASE IF NOT EXISTS `{settings.Name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ExecuteAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken: cancellationToken);
        await using var command = Command(sql, connection, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken: cancellationToken);
        await using var command = Command(sql, connection, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<MySqlDataReader, T> map, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken: cancellationToken);
        await using var command = Command(sql, connection, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>Which of <paramref name="tables"/> exist in the game database.</summary>
    public async Task<HashSet<string>> ExistingTablesAsync(IEnumerable<string> tables, CancellationToken cancellationToken = default)
    {
        var wanted = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = await QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @db",
            r => r.GetString(0),
            new Dictionary<string, object?> { ["@db"] = settings.Name },
            cancellationToken);
        return all.Where(wanted.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs SQL through the mariadb command-line client. LandSandBoat's .sql files rely on client features such as
    /// DELIMITER (for triggers), so they must go through the real client rather than a driver.
    /// </summary>
    /// <param name="continueOnError">Keep going past failing statements (mariadb --force) instead of stopping at the first.</param>
    public Task<ProcessResult> RunScriptAsync(string mariaDbClient, string script, CancellationToken cancellationToken = default, bool continueOnError = false) =>
        ProcessRunner.RunAsync(
            mariaDbClient,
            [
                $"--defaults-extra-file={WriteClientConfig()}",
                $"--database={settings.Name}",
                "--batch",
                .. continueOnError ? new[] { "--force" } : [],
            ],
            standardInput: script,
            cancellationToken: cancellationToken);

    /// <summary>Dumps tables (schema and data) to a .sql file with mariadb-dump.</summary>
    public async Task DumpAsync(string mariaDbDump, IEnumerable<string> tables, string outputFile, CancellationToken cancellationToken = default)
    {
        string[] arguments =
        [
            $"--defaults-extra-file={WriteClientConfig()}",
            "--hex-blob",
            "--add-drop-trigger",
            "--single-transaction",
            "--skip-dump-date",
            $"--result-file={outputFile}",
            settings.Name,
            .. tables,
        ];
        var result = await ProcessRunner.RunAsync(mariaDbDump, arguments, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException("Saving the database failed:\n" + result.CombinedOutput);
        }
    }

    /// <summary>
    /// Credentials for the command-line tools go in an options file so the password never appears in a process list.
    /// Returns its path.
    /// </summary>
    public string WriteClientConfig()
    {
        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(ClientConfigFile, $"""
            [client]
            user=root
            password={settings.RootPassword}
            host=127.0.0.1
            port={settings.Port}
            default-character-set=utf8mb4

            """);
        return ClientConfigFile;
    }

    private static MySqlCommand Command(string sql, MySqlConnection connection, IReadOnlyDictionary<string, object?>? parameters)
    {
        var command = new MySqlCommand(sql, connection);
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }
}
