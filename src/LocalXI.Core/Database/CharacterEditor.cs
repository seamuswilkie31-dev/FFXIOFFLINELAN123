using LocalXI.Core.Game;
using MySqlConnector;

namespace LocalXI.Core.Database;

public sealed record CharacterSummary(uint Id, string Name, string Account, int MainJob, int MainLevel)
{
    public string Label => MainLevel > 0 ? $"{Name}  ({Jobs.Abbreviation(MainJob)} {MainLevel}, {Account})" : $"{Name}  ({Account})";
}

public sealed record InventoryItem(int Slot, int ItemId, string Name, uint Quantity, bool Equipped);

public sealed record ItemInfo(int ItemId, string Name, int StackSize, bool Rare, bool Ex, bool GmOnly);

public sealed record JobLevel(int JobId, string Column, int Level, bool Unlocked)
{
    public string Abbreviation => Jobs.Abbreviation(JobId);
}

public sealed record FameRegion(string Column, string Name, bool Classic);

/// <summary>
/// Edits a character's saved data the way LandSandBoat itself stores it. The game server keeps logged-in characters
/// in memory and writes them back on logout, so edits are only safe while the character is offline; callers stop
/// the server first.
/// </summary>
public sealed class CharacterEditor(GameDatabase database)
{
    public const int GilItemId = 65535;
    private const int InventoryLocation = 0;
    private const int LinkshellType = 2;
    private const int CurrencyType = 8;
    private const long FlagGmOnly = 0x0002;
    private const long FlagEx = 0x4000;
    private const long FlagRare = 0x8000;
    private const long FlagNoRareCheck = 0x20000;
    public const uint MaxGil = 999_999_999;

    /// <summary>char_jobs columns by job id (xi.job), excluding Monstrosity.</summary>
    public static readonly IReadOnlyList<(int Id, string Column)> JobColumns =
    [
        (1, "war"), (2, "mnk"), (3, "whm"), (4, "blm"), (5, "rdm"), (6, "thf"), (7, "pld"), (8, "drk"), (9, "bst"), (10, "brd"),
        (11, "rng"), (12, "sam"), (13, "nin"), (14, "drg"), (15, "smn"), (16, "blu"), (17, "cor"), (18, "pup"), (19, "dnc"),
        (20, "sch"), (21, "geo"), (22, "run"),
    ];

    public static readonly IReadOnlyList<FameRegion> FameRegions =
    [
        new("fame_sandoria", "San d'Oria", true),
        new("fame_bastok", "Bastok", true),
        new("fame_windurst", "Windurst", true),
        new("fame_norg", "Norg", true),
        new("fame_aby_konschtat", "Abyssea - Konschtat", false),
        new("fame_aby_tahrongi", "Abyssea - Tahrongi", false),
        new("fame_aby_latheine", "Abyssea - La Theine", false),
        new("fame_aby_misareaux", "Abyssea - Misareaux", false),
        new("fame_aby_vunkerl", "Abyssea - Vunkerl", false),
        new("fame_aby_attohwa", "Abyssea - Attohwa", false),
        new("fame_aby_altepa", "Abyssea - Altepa", false),
        new("fame_aby_grauberg", "Abyssea - Grauberg", false),
        new("fame_aby_uleguerand", "Abyssea - Uleguerand", false),
        new("fame_adoulin", "Adoulin", false),
    ];

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken cancellationToken = default) =>
        database.QueryAsync(
            "SELECT c.charid, c.charname, COALESCE(a.login, ''), COALESCE(s.mjob, 0), COALESCE(s.mlvl, 0) " +
            "FROM chars c LEFT JOIN accounts a ON a.id = c.accid LEFT JOIN char_stats s ON s.charid = c.charid ORDER BY c.charname",
            r => new CharacterSummary(r.GetUInt32(0), r.GetString(1), r.GetString(2), Convert.ToInt32(r.GetValue(3)), Convert.ToInt32(r.GetValue(4))),
            cancellationToken: cancellationToken);

    /// <summary>True while the game server has a session for the character (it would overwrite any edit).</summary>
    public async Task<bool> IsOnlineAsync(uint charId, CancellationToken cancellationToken = default)
    {
        try
        {
            return Convert.ToInt64(await database.ScalarAsync("SELECT COUNT(*) FROM accounts_sessions WHERE charid = @id", Id(charId), cancellationToken)) > 0;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------ inventory and gil

    public async Task<uint> GetGilAsync(uint charId, CancellationToken cancellationToken = default) =>
        Convert.ToUInt32(await database.ScalarAsync(
            "SELECT COALESCE(SUM(quantity), 0) FROM char_inventory WHERE charid = @id AND location = 0 AND slot = 0 AND itemId = @gil",
            Args(charId, ("@gil", GilItemId)), cancellationToken));

    /// <summary>Gil is the currency item in inventory slot 0.</summary>
    public Task SetGilAsync(uint charId, uint gil, CancellationToken cancellationToken = default) =>
        database.ExecuteAsync(
            "INSERT INTO char_inventory (charid, location, slot, itemId, quantity) VALUES (@id, 0, 0, @gil, @amount) " +
            "ON DUPLICATE KEY UPDATE itemId = @gil, quantity = @amount",
            Args(charId, ("@gil", GilItemId), ("@amount", Math.Min(gil, MaxGil))), cancellationToken);

    public Task<IReadOnlyList<InventoryItem>> GetInventoryAsync(uint charId, CancellationToken cancellationToken = default) =>
        database.QueryAsync(
            "SELECT i.slot, i.itemId, COALESCE(b.name, CONCAT('item #', i.itemId)), i.quantity, " +
            "EXISTS(SELECT 1 FROM char_equip e WHERE e.charid = i.charid AND e.containerid = 0 AND e.slotid = i.slot) " +
            "FROM char_inventory i LEFT JOIN item_basic b ON b.itemid = i.itemId " +
            "WHERE i.charid = @id AND i.location = 0 AND i.slot > 0 ORDER BY i.slot",
            r => new InventoryItem(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), PrettyItem(r.GetString(2)), Convert.ToUInt32(r.GetValue(3)), Convert.ToInt64(r.GetValue(4)) != 0),
            Id(charId), cancellationToken);

    public async Task<int> GetInventoryCapacityAsync(uint charId, CancellationToken cancellationToken = default) =>
        Convert.ToInt32(await database.ScalarAsync("SELECT COALESCE(MAX(inventory), 30) FROM char_storage WHERE charid = @id", Id(charId), cancellationToken) ?? 30);

    /// <summary>Items that can be handed out: everything except linkshells (they need linkshell data) and gil itself.</summary>
    public Task<IReadOnlyList<ItemInfo>> SearchItemsAsync(string text, int limit = 200, CancellationToken cancellationToken = default)
    {
        var pattern = "%" + text.Trim().Replace(' ', '_').Replace("%", "").Replace("'", "") + "%";
        return database.QueryAsync(
            "SELECT itemid, name, stackSize, flags FROM item_basic " +
            "WHERE (name LIKE @p OR sortname LIKE @p) AND type NOT IN (@ls, @cur) AND itemid <> @gil ORDER BY name LIMIT @limit",
            r => ToItemInfo(r),
            new Dictionary<string, object?> { ["@p"] = pattern, ["@ls"] = LinkshellType, ["@cur"] = CurrencyType, ["@gil"] = GilItemId, ["@limit"] = limit },
            cancellationToken);
    }

    /// <summary>Puts an item stack in the first free inventory slot. Returns the slot used.</summary>
    public async Task<int> AddItemAsync(uint charId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        var item = (await database.QueryAsync(
            "SELECT itemid, name, stackSize, flags FROM item_basic WHERE itemid = @item AND type NOT IN (@ls, @cur) AND itemid <> @gil",
            ToItemInfo,
            new Dictionary<string, object?> { ["@item"] = itemId, ["@ls"] = LinkshellType, ["@cur"] = CurrencyType, ["@gil"] = GilItemId },
            cancellationToken)).FirstOrDefault() ?? throw new InvalidOperationException($"Item #{itemId} can't be added.");

        if (item.Rare && Convert.ToInt64(await database.ScalarAsync(
                "SELECT COUNT(*) FROM char_inventory WHERE charid = @id AND itemId = @item", Args(charId, ("@item", itemId)), cancellationToken)) > 0)
        {
            throw new InvalidOperationException($"{item.Name} is a Rare item and this character already has one.");
        }

        var capacity = await GetInventoryCapacityAsync(charId, cancellationToken);
        var used = (await database.QueryAsync(
            "SELECT slot FROM char_inventory WHERE charid = @id AND location = 0", r => Convert.ToInt32(r.GetValue(0)), Id(charId), cancellationToken)).ToHashSet();
        var slot = Enumerable.Range(1, capacity).FirstOrDefault(s => !used.Contains(s));
        if (slot == 0)
        {
            throw new InvalidOperationException($"The inventory is full ({capacity} of {capacity}). Remove something first.");
        }

        await database.ExecuteAsync(
            "INSERT INTO char_inventory (charid, location, slot, itemId, quantity) VALUES (@id, 0, @slot, @item, @qty)",
            Args(charId, ("@slot", slot), ("@item", itemId), ("@qty", Math.Clamp(quantity, 1, Math.Max(1, item.StackSize)))),
            cancellationToken);
        return slot;
    }

    public async Task RemoveItemAsync(uint charId, int slot, CancellationToken cancellationToken = default)
    {
        if (slot <= 0)
        {
            throw new InvalidOperationException("Use the gil box to change gil.");
        }

        if (Convert.ToInt64(await database.ScalarAsync(
                "SELECT COUNT(*) FROM char_equip WHERE charid = @id AND containerid = 0 AND slotid = @slot", Args(charId, ("@slot", slot)), cancellationToken)) > 0)
        {
            throw new InvalidOperationException("That item is equipped. Unequip it in game first, so the character isn't left wearing an item that no longer exists.");
        }

        await database.ExecuteAsync("DELETE FROM char_inventory WHERE charid = @id AND location = 0 AND slot = @slot", Args(charId, ("@slot", slot)), cancellationToken);
    }

    // ------------------------------------------------------------ jobs

    public async Task<IReadOnlyList<JobLevel>> GetJobsAsync(uint charId, CancellationToken cancellationToken = default)
    {
        var columns = string.Join(", ", JobColumns.Select(j => j.Column));
        var rows = await database.QueryAsync(
            $"SELECT unlocked, {columns} FROM char_jobs WHERE charid = @id",
            r => Enumerable.Range(0, JobColumns.Count + 1).Select(i => Convert.ToInt64(r.GetValue(i))).ToArray(),
            Id(charId), cancellationToken);
        var values = rows.FirstOrDefault() ?? throw new InvalidOperationException("This character has no job data.");
        var unlocked = values[0];
        return JobColumns.Select((job, i) => new JobLevel(job.Id, job.Column, (int)values[i + 1], (unlocked & (1L << job.Id)) != 0)).ToList();
    }

    /// <summary>
    /// Writes job levels and unlocks. EXP toward the next level is reset for changed jobs, and the level cap from
    /// limit breaks (genkai) is raised if needed so the character can actually hold the new levels.
    /// </summary>
    public async Task SetJobsAsync(uint charId, IReadOnlyList<JobLevel> jobs, int maxLevel, CancellationToken cancellationToken = default)
    {
        var before = (await GetJobsAsync(charId, cancellationToken)).ToDictionary(j => j.JobId);
        await using var connection = await database.OpenAsync(cancellationToken: cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long unlocked;
        await using (var read = new MySqlCommand("SELECT unlocked FROM char_jobs WHERE charid = @id", connection, transaction))
        {
            read.Parameters.AddWithValue("@id", charId);
            unlocked = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        var sets = new List<string>();
        var expResets = new List<string>();
        await using var update = new MySqlCommand { Connection = connection, Transaction = transaction };
        foreach (var job in jobs)
        {
            var column = JobColumns.First(j => j.Id == job.JobId).Column; // whitelisted
            var level = job.Unlocked ? Math.Clamp(job.Level, 1, maxLevel) : Math.Clamp(job.Level, 0, maxLevel);
            unlocked = job.Unlocked ? unlocked | (1L << job.JobId) : unlocked & ~(1L << job.JobId);
            sets.Add($"`{column}` = @{column}");
            update.Parameters.AddWithValue("@" + column, level);
            if (before.TryGetValue(job.JobId, out var old) && old.Level != level)
            {
                expResets.Add($"`{column}` = 0");
            }
        }

        var highest = jobs.Select(j => Math.Clamp(j.Level, 0, maxLevel)).DefaultIfEmpty(0).Max();
        update.CommandText = $"UPDATE char_jobs SET {string.Join(", ", sets)}, unlocked = @unlocked, genkai = GREATEST(genkai, @genkai) WHERE charid = @id";
        update.Parameters.AddWithValue("@unlocked", unlocked);
        update.Parameters.AddWithValue("@genkai", Math.Min(highest, maxLevel));
        update.Parameters.AddWithValue("@id", charId);
        await update.ExecuteNonQueryAsync(cancellationToken);

        if (expResets.Count > 0)
        {
            await using var exp = new MySqlCommand($"UPDATE char_exp SET {string.Join(", ", expResets)} WHERE charid = @id", connection, transaction);
            exp.Parameters.AddWithValue("@id", charId);
            await exp.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // ------------------------------------------------------------ fame

    public async Task<IReadOnlyDictionary<string, int>> GetFameAsync(uint charId, CancellationToken cancellationToken = default)
    {
        var columns = FameRegions.Select(f => f.Column).ToList();
        var rows = await database.QueryAsync(
            $"SELECT {string.Join(", ", columns)} FROM char_profile WHERE charid = @id",
            r => columns.Select((c, i) => (c, Convert.ToInt32(r.GetValue(i)))).ToDictionary(x => x.c, x => x.Item2),
            Id(charId), cancellationToken);
        return rows.FirstOrDefault() ?? columns.ToDictionary(c => c, _ => 0);
    }

    public Task SetFameAsync(uint charId, IReadOnlyDictionary<string, int> pointsByColumn, CancellationToken cancellationToken = default)
    {
        var allowed = FameRegions.Select(f => f.Column).ToHashSet();
        var changes = pointsByColumn.Where(p => allowed.Contains(p.Key)).ToList();
        if (changes.Count == 0)
        {
            return Task.CompletedTask;
        }

        var args = Id(charId);
        foreach (var (column, points) in changes)
        {
            args["@" + column] = Math.Clamp(points, 0, ushort.MaxValue);
        }

        return database.ExecuteAsync(
            $"UPDATE char_profile SET {string.Join(", ", changes.Select(c => $"`{c.Key}` = @{c.Key}"))} WHERE charid = @id",
            args, cancellationToken);
    }

    // ------------------------------------------------------------ quests

    public async Task<byte[]> GetQuestLogAsync(uint charId, CancellationToken cancellationToken = default) =>
        await database.ScalarAsync("SELECT quests FROM chars WHERE charid = @id", Id(charId), cancellationToken) as byte[] ?? [];

    /// <summary>
    /// Sets quest states. Quests reset or completed also lose their in-progress variables (Quest[area][id]...),
    /// as the game's own quest framework does; accepted quests keep theirs.
    /// </summary>
    public async Task SetQuestStatesAsync(uint charId, IReadOnlyList<(int Area, int Quest, QuestState State)> changes, CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var blob = await GetQuestLogAsync(charId, cancellationToken);
        foreach (var (area, quest, state) in changes)
        {
            blob = QuestLogBlob.Set(blob, area, quest, state);
        }

        await using var connection = await database.OpenAsync(cancellationToken: cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var write = new MySqlCommand("UPDATE chars SET quests = @blob WHERE charid = @id", connection, transaction))
        {
            write.Parameters.AddWithValue("@blob", blob);
            write.Parameters.AddWithValue("@id", charId);
            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (area, quest, _) in changes.Where(c => c.State != QuestState.Accepted))
        {
            await using var clear = new MySqlCommand("DELETE FROM char_vars WHERE charid = @id AND varname LIKE @prefix", connection, transaction);
            clear.Parameters.AddWithValue("@id", charId);
            clear.Parameters.AddWithValue("@prefix", $"Quest[{area}][{quest}]%");
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // ------------------------------------------------------------ home points

    public async Task<byte[]?> GetHomePointsAsync(uint charId, CancellationToken cancellationToken = default) =>
        await database.ScalarAsync("SELECT homepoints FROM char_unlocks WHERE charid = @id", Id(charId), cancellationToken) as byte[];

    public async Task SetHomePointsAsync(uint charId, IEnumerable<int> indices, bool registered, CancellationToken cancellationToken = default)
    {
        var blob = HomePointBlob.Set(await GetHomePointsAsync(charId, cancellationToken), indices, registered);
        await database.ExecuteAsync(
            "INSERT INTO char_unlocks (charid, homepoints) VALUES (@id, @blob) ON DUPLICATE KEY UPDATE homepoints = @blob",
            Args(charId, ("@blob", blob)), cancellationToken);
    }

    // ------------------------------------------------------------ GM

    /// <summary>The highest level LandSandBoat's chat commands ask for (cmdprops permission in scripts/commands).</summary>
    public const int MaxGmLevel = 5;

    /// <summary>Judge's helm, cuirass, gauntlets, cuisses and greaves: the GM outfit (item_basic flags GM_ONLY).</summary>
    public static readonly IReadOnlyList<int> JudgeOutfit = [12523, 12551, 12679, 12807, 12935];

    public async Task<int> GetGmLevelAsync(uint charId, CancellationToken cancellationToken = default) =>
        Convert.ToInt32(await database.ScalarAsync("SELECT gmlevel FROM chars WHERE charid = @id", Id(charId), cancellationToken) ?? 0);

    /// <summary>
    /// Sets the GM level (0 = player). Taking it away also switches off GM mode and hiding, which the server
    /// remembers in char_flags across logins.
    /// </summary>
    public async Task SetGmLevelAsync(uint charId, int level, CancellationToken cancellationToken = default)
    {
        level = Math.Clamp(level, 0, MaxGmLevel);
        await database.ExecuteAsync("UPDATE chars SET gmlevel = @level WHERE charid = @id", Args(charId, ("@level", level)), cancellationToken);
        if (level == 0)
        {
            await database.ExecuteAsync("UPDATE char_flags SET gmModeEnabled = 0, gmHiddenEnabled = 0 WHERE charid = @id", Id(charId), cancellationToken);
        }
    }

    /// <summary>Adds the pieces of the Judge's outfit the character doesn't already own. Returns how many were added.</summary>
    public async Task<int> AddJudgeOutfitAsync(uint charId, CancellationToken cancellationToken = default)
    {
        if (await GetGmLevelAsync(charId, cancellationToken) == 0)
        {
            throw new InvalidOperationException("The Judge's outfit is GM-only gear. Set a GM level for this character first.");
        }

        var owned = (await database.QueryAsync(
            "SELECT DISTINCT itemId FROM char_inventory WHERE charid = @id", r => Convert.ToInt32(r.GetValue(0)), Id(charId), cancellationToken)).ToHashSet();
        var missing = JudgeOutfit.Where(id => !owned.Contains(id)).ToList();

        var capacity = await GetInventoryCapacityAsync(charId, cancellationToken);
        var used = Convert.ToInt32(await database.ScalarAsync(
            "SELECT COUNT(*) FROM char_inventory WHERE charid = @id AND location = 0 AND slot BETWEEN 1 AND @capacity",
            Args(charId, ("@capacity", capacity)), cancellationToken));
        if (capacity - used < missing.Count)
        {
            throw new InvalidOperationException($"The outfit needs {missing.Count} free inventory slots and there are {capacity - used}. Remove something first.");
        }

        foreach (var itemId in missing)
        {
            await AddItemAsync(charId, itemId, 1, cancellationToken);
        }

        return missing.Count;
    }

    // ------------------------------------------------------------ helpers

    private static ItemInfo ToItemInfo(MySqlDataReader r)
    {
        var flags = Convert.ToInt64(r.GetValue(3));
        return new ItemInfo(Convert.ToInt32(r.GetValue(0)), PrettyItem(r.GetString(1)), Convert.ToInt32(r.GetValue(2)),
            (flags & FlagRare) != 0 && (flags & FlagNoRareCheck) == 0, (flags & FlagEx) != 0, (flags & FlagGmOnly) != 0);
    }

    /// <summary>item_basic names are lower_snake_case ("pile_of_chocobo_bedding").</summary>
    public static string PrettyItem(string name) =>
        string.Join(' ', name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static Dictionary<string, object?> Id(uint charId) => new() { ["@id"] = charId };

    private static Dictionary<string, object?> Args(uint charId, params (string Name, object? Value)[] extra)
    {
        var args = Id(charId);
        foreach (var (name, value) in extra)
        {
            args[name] = value;
        }

        return args;
    }
}
