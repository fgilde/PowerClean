using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

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
    public ObservableCollection<DuplicateGroup> Groups { get; } = new();

    [ObservableProperty]
    private long _wastedTotal;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Wähle Ordner und starte die Suche.";

    [ObservableProperty]
    private int _filesScanned;

    [RelayCommand]
    public async Task FindAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        Groups.Clear();
        StatusText = "Suche nach Duplikaten...";

        var progress = new Progress<ScanProgress>(p => FilesScanned = p.FilesSoFar);
        try
        {
            var minBytes = _settings.DuplicateMinSizeKb * 1024;
            var result = await _finder.FindAsync(SearchRoots.ToList(), minBytes, progress, _cts.Token);
            foreach (var g in result) Groups.Add(g);
            WastedTotal = result.Sum(g => g.WastedBytes);
            StatusText = $"{result.Count} Duplikat-Gruppen gefunden — {Cleaner.Core.Utils.ByteFormatter.Format(WastedTotal)} verschwendet.";
        }
        catch (OperationCanceledException) { StatusText = "Abgebrochen."; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

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
                if (g.Paths.Remove(path))
                {
                    if (g.Paths.Count <= 1) Groups.Remove(g);
                    break;
                }
            }
            WastedTotal = Groups.Sum(g => g.WastedBytes);
        }
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
