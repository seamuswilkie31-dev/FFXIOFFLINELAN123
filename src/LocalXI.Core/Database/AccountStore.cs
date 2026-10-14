namespace LocalXI.Core.Database;

public sealed record AccountInfo(uint Id, string Login, int Characters);

public sealed record CharacterInfo(string Name, string Account, int MainJob, int MainLevel);

/// <summary>
/// Manages server accounts directly in the database. Mirrors LandSandBoat's own account creation
/// (src/login/auth_session.cpp): ids start at 1000 and passwords are stored as bcrypt hashes.
/// </summary>
public sealed class AccountStore(GameDatabase database)
{
    private const int StatusNormal = 1;
    private const int PrivilegeUser = 1;

    public async Task<bool> ExistsAsync(string login, CancellationToken cancellationToken = default) =>
        Convert.ToInt64(await database.ScalarAsync("SELECT COUNT(*) FROM accounts WHERE login = @login", Args(login), cancellationToken)) > 0;

    public async Task CreateAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var maxId = Convert.ToUInt32(await database.ScalarAsync("SELECT COALESCE(MAX(id), 0) FROM accounts", cancellationToken: cancellationToken));
        var id = Math.Max(maxId + 1, 1000);
        await database.ExecuteAsync(
            "INSERT INTO accounts (id, login, password, timecreate, timelastmodify, status, priv) " +
            "VALUES (@id, @login, @password, NOW(), NOW(), @status, @priv)",
            new Dictionary<string, object?>
            {
                ["@id"] = id,
                ["@login"] = login,
                ["@password"] = BCrypt.Net.BCrypt.HashPassword(password),
                ["@status"] = StatusNormal,
                ["@priv"] = PrivilegeUser,
            },
            cancellationToken);
    }

    public Task SetPasswordAsync(string login, string password, CancellationToken cancellationToken = default) =>
        database.ExecuteAsync(
            "UPDATE accounts SET password = @password, timelastmodify = NOW() WHERE login = @login",
            new Dictionary<string, object?> { ["@login"] = login, ["@password"] = BCrypt.Net.BCrypt.HashPassword(password) },
            cancellationToken);

    /// <summary>Creates the account, or resets its password if it already exists.</summary>
    public async Task EnsureAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(login, cancellationToken))
        {
            await SetPasswordAsync(login, password, cancellationToken);
        }
        else
        {
            await CreateAsync(login, password, cancellationToken);
        }
    }

    public Task<IReadOnlyList<AccountInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        database.QueryAsync(
            "SELECT a.id, a.login, (SELECT COUNT(*) FROM chars c WHERE c.accid = a.id) FROM accounts a ORDER BY a.login",
            r => new AccountInfo(r.GetUInt32(0), r.GetString(1), Convert.ToInt32(r.GetValue(2))),
            cancellationToken: cancellationToken);

    public Task<IReadOnlyList<CharacterInfo>> ListCharactersAsync(CancellationToken cancellationToken = default) =>
        database.QueryAsync(
            "SELECT c.charname, COALESCE(a.login, ''), COALESCE(s.mjob, 0), COALESCE(s.mlvl, 0) " +
            "FROM chars c LEFT JOIN accounts a ON a.id = c.accid LEFT JOIN char_stats s ON s.charid = c.charid ORDER BY c.charname",
            r => new CharacterInfo(r.GetString(0), r.GetString(1), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3))),
            cancellationToken: cancellationToken);

    private static Dictionary<string, object?> Args(string login) => new() { ["@login"] = login };
}
