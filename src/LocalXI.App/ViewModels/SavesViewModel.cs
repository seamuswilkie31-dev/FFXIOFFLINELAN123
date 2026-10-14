using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalXI.Core.Config;
using LocalXI.Core.Database;
using LocalXI.Core.Game;
using LocalXI.Core.Saves;
using Microsoft.Win32;

namespace LocalXI.App.ViewModels;

public sealed record SaveRow(string Kind, SaveFileInfo File)
{
    public string Name => File.FileName;
    public string Date => File.Modified.ToString("g");
    public string Size => File.SizeBytes < 1024 * 1024 ? $"{Math.Max(1, File.SizeBytes / 1024)} KB" : $"{File.SizeBytes / 1024.0 / 1024.0:0.0} MB";

    public string Characters => File.Manifest is { Characters.Count: > 0 } manifest
        ? string.Join(", ", manifest.Characters.Select(c => c.Level > 0 ? $"{c.Name} ({c.Job} {c.Level})" : c.Name))
        : "(no characters)";
}

public sealed record CharacterRow(CharacterInfo Info)
{
    public string Name => Info.Name;
    public string Account => Info.Account;
    public string Job => Info.MainLevel > 0 ? $"{Jobs.Abbreviation(Info.MainJob)} {Info.MainLevel}" : "";
}

public sealed partial class SavesViewModel(MainViewModel main) : PageViewModel(main)
{
    public ObservableCollection<SaveRow> Files { get; } = [];
    public ObservableCollection<CharacterRow> Characters { get; } = [];

    [ObservableProperty]
    private SaveRow? _selectedFile;

    [ObservableProperty]
    private string _charactersNote = "";

    public bool IsJoin => Launcher.Config.Mode == PlayMode.Join;
    public string SavesFolder => Launcher.Paths.SavesDir;
    public string BackupsFolder => Launcher.Paths.BackupsDir;

    public override void OnShown()
    {
        OnPropertyChanged(nameof(IsJoin));
        RefreshFiles();
        _ = RefreshCharactersAsync();
    }

    [RelayCommand]
    private void RefreshFiles()
    {
        Files.Clear();
        var saves = Launcher.Saves;
        foreach (var file in saves.List(Launcher.Paths.SavesDir))
        {
            Files.Add(new SaveRow("Save", file));
        }

        foreach (var file in saves.List(Launcher.Paths.BackupsDir))
        {
            Files.Add(new SaveRow("Backup", file));
        }
    }

    private async Task RefreshCharactersAsync()
    {
        Characters.Clear();
        if (IsJoin || !Launcher.Config.SetupComplete)
        {
            CharactersNote = IsJoin ? "Your characters live on your host's PC while you are joining a friend." : "";
            return;
        }

        try
        {
            // The database is small and starts in a couple of seconds; start it so the list is never empty for no reason.
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            foreach (var character in await new AccountStore(Launcher.Database).ListCharactersAsync())
            {
                Characters.Add(new CharacterRow(character));
            }

            CharactersNote = Characters.Count == 0 ? "No characters yet - press Play and create one!" : "";
        }
        catch (Exception e)
        {
            CharactersNote = "Couldn't read characters: " + e.Message;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export characters",
            Filter = "LocalXI save (*.xisave)|*.xisave",
            InitialDirectory = Launcher.Paths.SavesDir,
            FileName = $"{Launcher.Config.Account.Username}-{DateTime.Now:yyyy-MM-dd}{SaveManager.Extension}",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await Main.RunAsync(async progress =>
        {
            progress.Report("Exporting your characters...");
            var manifest = await Launcher.ExportSaveAsync(dialog.FileName);
            progress.Report($"Exported {manifest.Characters.Count} character(s) to {dialog.FileName}");
        });
        RefreshFiles();
    }

    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a save",
            Filter = "LocalXI save (*.xisave)|*.xisave",
            InitialDirectory = Launcher.Paths.SavesDir,
        };
        if (dialog.ShowDialog() == true)
        {
            await ImportAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private Task ImportSelectedAsync() => SelectedFile is null ? Task.CompletedTask : ImportAsync(SelectedFile.File.Path);

    private async Task ImportAsync(string path)
    {
        var manifest = SaveManager.TryReadManifest(path);
        if (manifest is null)
        {
            MessageBox.Show("That file isn't a LocalXI save.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var names = manifest.Characters.Count > 0 ? string.Join(", ", manifest.Characters.Select(c => c.Name)) : "no characters";
        var question =
            $"Load this save from {manifest.CreatedUtc.ToLocalTime():g}?\n\nIt contains: {names}\n\n" +
            "This replaces every character currently on this PC. Your current characters are backed up first, " +
            "so you can switch back by importing that backup.";
        if (MessageBox.Show(question, "Import save", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        await Main.RunAsync(progress => Launcher.ImportSaveAsync(path, progress));
        OnShown();
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        await Main.RunAsync(async progress =>
        {
            await Launcher.MariaDb.StartAsync(Launcher.Database, CancellationToken.None);
            var file = await Launcher.BackupAsync("manual", progress);
            progress.Report(file is null ? "Backup failed - see Logs." : $"Backed up to {file}");
        });
        RefreshFiles();
    }

    [RelayCommand]
    private void OpenSavesFolder() => MainViewModel.OpenFolder(Launcher.Paths.SavesDir);

    [RelayCommand]
    private void OpenBackupsFolder() => MainViewModel.OpenFolder(Launcher.Paths.BackupsDir);
}
