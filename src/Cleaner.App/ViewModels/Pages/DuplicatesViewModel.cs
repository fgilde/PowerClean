using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

/// <summary>Eine Datei innerhalb einer Duplikat-Gruppe (mit Auswahl-Checkbox).</summary>
public sealed partial class DuplicateFileViewModel : ObservableObject
{
    public DuplicateFileViewModel(string path) => Path = path;

    public string Path { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Eine Duplikat-Gruppe mit auf-/zuklappbarem Zustand und auswählbaren Dateien.</summary>
public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(DuplicateGroup group, Action selectionChanged)
    {
        Group = group;
        Files = new ObservableCollection<DuplicateFileViewModel>(group.Paths.Select(p => new DuplicateFileViewModel(p)));
        foreach (var f in Files)
            f.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DuplicateFileViewModel.IsSelected))
                    selectionChanged();
            };
    }

    public DuplicateGroup Group { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; }

    public long FileSize => Group.FileSize;
    public string Hash => Group.Hash;
    public int CopyCount => Files.Count;
    public long WastedBytes => FileSize * Math.Max(0, Files.Count - 1);

    [ObservableProperty]
    private bool _isExpanded;

    public void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(CopyCount));
        OnPropertyChanged(nameof(WastedBytes));
    }
}

public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly IDuplicateFinder _finder;
    private readonly IFileSystemOperations _fs;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    public DuplicatesViewModel(IDuplicateFinder finder, IFileSystemOperations fs, AppSettings settings)
    {
        _finder = finder;
        _fs = fs;
        _settings = settings;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SearchRoots.Add(Path.Combine(profile, "Downloads"));
        SearchRoots.Add(Path.Combine(profile, "Documents"));
        SearchRoots.Add(Path.Combine(profile, "Pictures"));
    }

    public ObservableCollection<string> SearchRoots { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private long _wastedTotal;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Wähle Ordner und starte die Suche.";

    [ObservableProperty]
    private int _filesScanned;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    public bool HasSelection => SelectedCount > 0;

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    public async Task FindAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        Groups.Clear();
        RecomputeSelection();
        StatusText = "Suche nach Duplikaten...";

        var progress = new Progress<ScanProgress>(p => FilesScanned = p.FilesSoFar);
        try
        {
            var minBytes = _settings.DuplicateMinSizeKb * 1024;
            var result = await _finder.FindAsync(SearchRoots.ToList(), minBytes, progress, _cts.Token);
            foreach (var g in result) Groups.Add(new DuplicateGroupViewModel(g, RecomputeSelection));
            WastedTotal = result.Sum(g => g.WastedBytes);
            StatusText = $"{result.Count} Duplikat-Gruppen gefunden — {Cleaner.Core.Utils.ByteFormatter.Format(WastedTotal)} verschwendet.";
        }
        catch (OperationCanceledException) { StatusText = "Abgebrochen."; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void ExpandAll()
    {
        foreach (var g in Groups) g.IsExpanded = true;
    }

    [RelayCommand]
    public void CollapseAll()
    {
        foreach (var g in Groups) g.IsExpanded = false;
    }

    /// <summary>Wählt in jeder Gruppe alle Kopien außer der ersten aus — ein Klick, maximaler Gewinn.</summary>
    [RelayCommand]
    public void SelectAllButFirst()
    {
        foreach (var g in Groups)
            for (var i = 0; i < g.Files.Count; i++)
                g.Files[i].IsSelected = i > 0;
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var g in Groups)
            foreach (var f in g.Files)
                f.IsSelected = false;
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        var selected = Groups.SelectMany(g => g.Files.Where(f => f.IsSelected).Select(f => (Group: g, File: f))).ToList();
        if (selected.Count == 0) return;

        // Schutz: Nie eine komplette Gruppe löschen — mindestens eine Kopie bleibt.
        var fullySelected = Groups.Where(g => g.Files.Count > 0 && g.Files.All(f => f.IsSelected)).ToList();
        if (fullySelected.Count > 0)
        {
            MessageBox.Show(
                $"In {fullySelected.Count} Gruppe(n) sind ALLE Kopien ausgewählt — dann bliebe keine Datei übrig.\n" +
                "Bitte in jeder Gruppe mindestens eine Kopie behalten (Tipp: „Auto-Auswahl“).",
                "Duplikate löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bytes = selected.Sum(s => s.Group.FileSize);
        var msg = _settings.UseRecycleBin
            ? $"{selected.Count} Dateien ({Cleaner.Core.Utils.ByteFormatter.Format(bytes)}) in den Papierkorb verschieben?"
            : $"{selected.Count} Dateien ({Cleaner.Core.Utils.ByteFormatter.Format(bytes)}) ENDGÜLTIG löschen?";
        if (MessageBox.Show(msg, "Ausgewählte Duplikate löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var failed = 0;
        foreach (var (group, file) in selected)
        {
            if (_fs.DeleteFile(file.Path, _settings.UseRecycleBin))
                RemoveFileFromGroup(group, file);
            else
                failed++;
        }

        WastedTotal = Groups.Sum(g => g.WastedBytes);
        RecomputeSelection();
        StatusText = $"{selected.Count - failed} Dateien gelöscht" +
                     (failed > 0 ? $", {failed} fehlgeschlagen" : "") +
                     $" — noch {Cleaner.Core.Utils.ByteFormatter.Format(WastedTotal)} verschwendet.";
    }

    [RelayCommand]
    public void DeleteDuplicate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var msg = _settings.UseRecycleBin
            ? $"Datei in den Papierkorb verschieben?\n\n{path}"
            : $"Datei ENDGÜLTIG löschen?\n\n{path}";
        if (MessageBox.Show(msg, "Duplikat löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (_fs.DeleteFile(path, _settings.UseRecycleBin))
        {
            foreach (var g in Groups.ToList())
            {
                var file = g.Files.FirstOrDefault(f => f.Path == path);
                if (file != null)
                {
                    RemoveFileFromGroup(g, file);
                    break;
                }
            }
            WastedTotal = Groups.Sum(g => g.WastedBytes);
            RecomputeSelection();
        }
    }

    private void RemoveFileFromGroup(DuplicateGroupViewModel group, DuplicateFileViewModel file)
    {
        group.Files.Remove(file);
        group.Group.Paths.Remove(file.Path);
        group.RaiseCountsChanged();
        if (group.Files.Count <= 1)
            Groups.Remove(group);
    }

    private void RecomputeSelection()
    {
        var files = Groups.SelectMany(g => g.Files.Where(f => f.IsSelected).Select(f => g.FileSize)).ToList();
        SelectedCount = files.Count;
        SelectedBytes = files.Sum();
    }

    [RelayCommand]
    public void AddSearchRoot()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner zum Durchsuchen hinzufügen" };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            SearchRoots.Add(dlg.FolderName);
    }

    [RelayCommand]
    public void RemoveSearchRoot(string? root)
    {
        if (root is not null) SearchRoots.Remove(root);
    }
}
