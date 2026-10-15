using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Server;

namespace LocalXI.App.ViewModels;

public sealed partial class JobRow(int jobId, string column, bool available) : ObservableObject
{
    public int JobId { get; } = jobId;
    public string Column { get; } = column;
    public string Name => $"{Jobs.Name(JobId)} ({Jobs.Abbreviation(JobId)})";
    public bool Available { get; } = available;

    [ObservableProperty]
    private int _level;

    [ObservableProperty]
    private bool _unlocked;
}

public sealed partial class FameRow(string column, string name) : ObservableObject
{
    public string Column { get; } = column;
    public string Name { get; } = name;

    [ObservableProperty]
    private int _level = 1;
}

public sealed partial class QuestRow(QuestInfo quest, QuestState original) : ObservableObject
{
    public QuestInfo Quest { get; } = quest;
    public string Name => Quest.Name;
    public QuestState Original { get; set; } = original;

    [ObservableProperty]
    private QuestState _state = original;
}

public sealed record GmLevelOption(int Level, string Label);

/// <summary>
/// Per-character editing (inventory, gil, jobs, fame, quests, home points, GM level) plus the world's EXP rate.
/// Edits need the server stopped; the first one each session backs everything up.
/// </summary>
public sealed partial class ModifiersViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<QuestRow> _allQuests = [];

    public ObservableCollection<CharacterSummary> Characters { get; } = [];
    public ObservableCollection<InventoryItem> Inventory { get; } = [];
    public ObservableCollection<ItemInfo> SearchResults { get; } = [];
    public ObservableCollection<JobRow> JobRows { get; } = [];
    public ObservableCollection<FameRow> FameRows { get; } = [];
    public ObservableCollection<QuestRow> Quests { get; } = [];

    public IReadOnlyList<double> ExpRates => ServerSettingsFiles.ExpRates;
    public IReadOnlyList<int> FameLevels { get; } = Enumerable.Range(1, 9).ToList();
    public IReadOnlyList<QuestState> QuestStates { get; } = Enum.GetValues<QuestState>();
    public IReadOnlyList<string> QuestAreas => GameCatalog.QuestAreaNames;

    /// <summary>What each level unlocks, from the permission values in the server's scripts/commands.</summary>
    public IReadOnlyList<GmLevelOption> GmLevels { get; } =
    [
        new(0, "0 - Player (no GM commands)"),
        new(1, "1 - GM: almost every command (!additem, !goto, !zone, !speed, !hide...)"),
        new(2, "2 - GM, plus a few combat checks"),
        new(3, "3 - Senior GM, plus monster, fame and variable tools"),
        new(4, "4 - Lead GM, plus !exec and script reloads"),
        new(5, "5 - Administrator: everything, including developer tools"),
    ];

    public bool CanEdit => Launcher.Config.RunsLocalServer && Launcher.Config.SetupComplete;
    public bool IsClassic => Launcher.Config.Server.Era == GameEra.Classic75;
    public int MaxLevel => IsClassic ? 75 : 99;
    public string LevelHint => IsClassic ? "Levels 1-75 (classic era). Geomancer and Rune Fencer belong to modern mode." : "Levels 1-99.";

    [ObservableProperty]
    private CharacterSummary? _selectedCharacter;

    [ObservableProperty]
    private bool _serverRunning;

    [ObservableProperty]
    private string _loadNote = "";

    [ObservableProperty]
    private double _expRate = 1.0;

    [ObservableProperty]
    private string _gil = "0";

    [ObservableProperty]
    private string _inventorySummary = "";

    [ObservableProperty]
    private InventoryItem? _selectedInventoryItem;

    [ObservableProperty]
    private string _itemSearch = "";

    [ObservableProperty]
    private ItemInfo? _selectedSearchResult;

    [ObservableProperty]
    private string _addQuantity = "1";

    [ObservableProperty]
    private string _derivedFame = "";

    [ObservableProperty]
    private int _selectedQuestArea;

    [ObservableProperty]
    private string _questFilter = "";

    [ObservableProperty]
    private string _questSummary = "";

    [ObservableProperty]
    private string _homePointSummary = "";

    [ObservableProperty]
    private GmLevelOption? _selectedGmLevel;

    [ObservableProperty]
    private bool _gmMode;

    [ObservableProperty]
    private bool _newAdventurer;

    public override void OnShown()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsClassic));
        OnPropertyChanged(nameof(MaxLevel));
        OnPropertyChanged(nameof(LevelHint));
        ExpRate = Launcher.Config.Server.ExpRate;
        ServerRunning = Launcher.Config.RunsLocalServer && Launcher.Server.IsRunning;
        _ = LoadCharactersAsync();
    }

    partial void OnSelectedCharacterChanged(CharacterSummary? value) => _ = LoadCharacterAsync();

    partial void OnSelectedQuestAreaChanged(int value) => FilterQuests();

    partial void OnQuestFilterChanged(string value) => FilterQuests();

    // ------------------------------------------------------------ loading

    [RelayCommand]
    private async Task LoadCharactersAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        try
        {
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            var previous = SelectedCharacter?.Id;
            Characters.Clear();
            foreach (var character in await Launcher.Characters.ListCharactersAsync())
            {
                Characters.Add(character);
            }

            SelectedCharacter = Characters.FirstOrDefault(c => c.Id == previous) ?? Characters.FirstOrDefault();
            LoadNote = Characters.Count == 0 ? "No characters yet. Press Play and create one first." : "";
        }
        catch (Exception e)
        {
            LoadNote = "Couldn't read characters: " + e.Message;
        }
    }

    private async Task LoadCharacterAsync()
    {
        if (SelectedCharacter is not { } character)
        {
            return;
        }

        try
        {
            var editor = Launcher.Characters;
            var catalog = Launcher.Catalog;
            await LoadInventoryAsync(editor, character.Id);

            JobRows.Clear();
            foreach (var job in await editor.GetJobsAsync(character.Id))
            {
                JobRows.Add(new JobRow(job.JobId, job.Column, !IsClassic || Jobs.IsClassic(job.JobId)) { Level = job.Level, Unlocked = job.Unlocked });
            }

            FameRows.Clear();
            var fame = await editor.GetFameAsync(character.Id);
            foreach (var region in CharacterEditor.FameRegions.Where(r => r.Classic || !IsClassic))
            {
                var row = new FameRow(region.Column, region.Name) { Level = catalog.FameLevel(fame.GetValueOrDefault(region.Column)) };
                row.PropertyChanged += OnFameChanged;
                FameRows.Add(row);
            }

            UpdateDerivedFame();

            var log = await editor.GetQuestLogAsync(character.Id);
            _allQuests = catalog.Quests.Select(q => new QuestRow(q, QuestLogBlob.Get(log, q.Area, q.Id))).ToList();
            FilterQuests();

            var homePoints = await editor.GetHomePointsAsync(character.Id);
            var registered = catalog.HomePoints.Count(h => HomePointBlob.IsRegistered(homePoints, h.Index));
            HomePointSummary = catalog.HomePoints.Count == 0
                ? "The server's home point list couldn't be read."
                : $"{registered} of {catalog.HomePoints.Count} home point crystals registered.";

            var gm = await editor.GetGmSettingsAsync(character.Id);
            SelectedGmLevel = GmLevels[Math.Clamp(gm.Level, 0, CharacterEditor.MaxGmLevel)];
            GmMode = gm.GmMode;
            NewAdventurer = gm.NewAdventurer;
        }
        catch (Exception e)
        {
            LoadNote = $"Couldn't read {character.Name}: {e.Message}";
        }
    }

    private async Task LoadInventoryAsync(CharacterEditor editor, uint charId)
    {
        Gil = (await editor.GetGilAsync(charId)).ToString("N0", CultureInfo.CurrentCulture);
        Inventory.Clear();
        foreach (var item in await editor.GetInventoryAsync(charId))
        {
            Inventory.Add(item);
        }

        InventorySummary = $"{Inventory.Count} of {await editor.GetInventoryCapacityAsync(charId)} inventory slots used";
    }

    private void FilterQuests()
    {
        var filter = QuestFilter.Trim();
        Quests.Clear();
        foreach (var row in _allQuests.Where(q => q.Quest.Area == SelectedQuestArea &&
                                                  (filter.Length == 0 || q.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))))
        {
            Quests.Add(row);
        }

        var inArea = _allQuests.Where(q => q.Quest.Area == SelectedQuestArea).ToList();
        QuestSummary = $"{inArea.Count(q => q.State == QuestState.Completed)} of {inArea.Count} completed in {QuestAreas[SelectedQuestArea]}";
    }

    private void OnFameChanged(object? sender, PropertyChangedEventArgs e) => UpdateDerivedFame();

    /// <summary>Jeuno and Selbina/Rabao have no fame of their own; the game derives it (lua_base_entity.cpp getFame).</summary>
    private void UpdateDerivedFame()
    {
        int Points(string column) => FameRows.FirstOrDefault(f => f.Column == column) is { } row ? Launcher.Catalog.FameRankPoints[row.Level - 1] : 0;
        var (sandoria, bastok, windurst) = (Points("fame_sandoria"), Points("fame_bastok"), Points("fame_windurst"));
        var catalog = Launcher.Catalog;
        DerivedFame = $"Jeuno: level {catalog.FameLevel((sandoria + bastok + windurst) / 2)}   ·   " +
                      $"Selbina / Rabao: level {catalog.FameLevel((sandoria + bastok) * 2 / 3)}   (worked out from the three nations)";
    }

    // ------------------------------------------------------------ editing

    /// <summary>Runs one change with the safety checks: server stopped, character offline, backup taken.</summary>
    private async Task EditAsync(string done, Func<CharacterEditor, uint, Task> change)
    {
        if (SelectedCharacter is not { } character)
        {
            return;
        }

        var ok = await Main.RunAsync(async progress =>
        {
            await Launcher.BeginCharacterEditAsync(progress);
            var editor = Launcher.Characters;
            if (await editor.IsOnlineAsync(character.Id))
            {
                throw new InvalidOperationException($"{character.Name} is logged in. Log out and stop the server first.");
            }

            await change(editor, character.Id);
            progress.Report(done);
        });

        ServerRunning = Launcher.Server.IsRunning;
        if (ok)
        {
            await LoadCharacterAsync();
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        await Main.RunAsync(progress => Launcher.StopServerAsync(progress));
        ServerRunning = Launcher.Server.IsRunning;
    }

    [RelayCommand]
    private void SaveExpRate()
    {
        Launcher.Config.Server.ExpRate = ExpRate;
        Launcher.SaveConfig();
        Main.Status = Launcher.Server.IsRunning
            ? $"EXP rate set to x{ExpRate}. It applies the next time the server starts."
            : $"EXP rate set to x{ExpRate}.";
    }

    [RelayCommand]
    private Task SetGilAsync()
    {
        if (!uint.TryParse(Gil.Replace(",", "").Replace(".", "").Replace(" ", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var gil))
        {
            MessageBox.Show("Enter an amount of gil (0 to 999,999,999).", "Gil", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        return EditAsync($"Gil set to {Math.Min(gil, CharacterEditor.MaxGil):N0}.", (editor, id) => editor.SetGilAsync(id, gil));
    }

    [RelayCommand]
    private async Task SearchItemsAsync()
    {
        if (ItemSearch.Trim().Length < 2)
        {
            return;
        }

        SearchResults.Clear();
        foreach (var item in await Launcher.Characters.SearchItemsAsync(ItemSearch))
        {
            SearchResults.Add(item);
        }

        Main.Status = SearchResults.Count == 200 ? "Showing the first 200 matches; type more to narrow it down." : $"{SearchResults.Count} items found.";
    }

    [RelayCommand]
    private Task AddItemAsync()
    {
        if (SelectedSearchResult is not { } item)
        {
            return Task.CompletedTask;
        }

        var quantity = int.TryParse(AddQuantity, out var q) ? q : 1;
        return EditAsync($"Added {item.Name}.", (editor, id) => editor.AddItemAsync(id, item.ItemId, quantity));
    }

    [RelayCommand]
    private Task RemoveItemAsync() =>
        SelectedInventoryItem is { } item
            ? EditAsync($"Removed {item.Name}.", (editor, id) => editor.RemoveItemAsync(id, item.Slot))
            : Task.CompletedTask;

    [RelayCommand]
    private Task SaveJobsAsync()
    {
        var jobs = JobRows.Select(r => new JobLevel(r.JobId, r.Column, r.Level, r.Unlocked)).ToList();
        return EditAsync("Jobs saved.", (editor, id) => editor.SetJobsAsync(id, jobs, MaxLevel));
    }

    [RelayCommand]
    private Task SaveFameAsync()
    {
        var points = FameRows.ToDictionary(r => r.Column, r => Launcher.Catalog.FameRankPoints[r.Level - 1]);
        return EditAsync("Fame saved.", (editor, id) => editor.SetFameAsync(id, points));
    }

    [RelayCommand]
    private Task SaveQuestsAsync()
    {
        var changes = _allQuests.Where(q => q.State != q.Original).Select(q => (q.Quest.Area, q.Quest.Id, q.State)).ToList();
        if (changes.Count == 0)
        {
            Main.Status = "No quest changes to save.";
            return Task.CompletedTask;
        }

        return EditAsync($"{changes.Count} quest(s) updated.", (editor, id) => editor.SetQuestStatesAsync(id, changes));
    }

    [RelayCommand]
    private Task RegisterAllHomePointsAsync() =>
        EditAsync("Every home point crystal is registered.",
            (editor, id) => editor.SetHomePointsAsync(id, Launcher.Catalog.HomePoints.Select(h => h.Index), registered: true));

    [RelayCommand]
    private Task ClearHomePointsAsync() =>
        EditAsync("Home point registrations cleared.",
            (editor, id) => editor.SetHomePointsAsync(id, Launcher.Catalog.HomePoints.Select(h => h.Index), registered: false));

    [RelayCommand]
    private Task SaveGmAsync() =>
        SelectedGmLevel is { } level
            ? EditAsync(level.Level == 0 ? "GM rights removed." : $"GM level {level.Level} saved{(GmMode ? ", with GM mode on" : "")}.",
                (editor, id) => editor.SetGmSettingsAsync(id, new GmSettings(level.Level, GmMode, NewAdventurer)))
            : Task.CompletedTask;

    [RelayCommand]
    private Task AddJudgeOutfitAsync() =>
        EditAsync("Judge's outfit added to the inventory.", async (editor, id) =>
        {
            if (await editor.AddJudgeOutfitAsync(id) == 0)
            {
                throw new InvalidOperationException("This character already has every piece of the Judge's outfit.");
            }
        });
}
