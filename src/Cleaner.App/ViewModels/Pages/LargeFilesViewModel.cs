using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class LargeFilesViewModel : ObservableObject
{
    private readonly ILargeFilesFinder _finder;
    private readonly IFileSystemOperations _fs;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    public LargeFilesViewModel(ILargeFilesFinder finder, IFileSystemOperations fs, AppSettings settings)
    {
        _finder = finder;
        _fs = fs;
        _settings = settings;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SearchRoots.Add(profile);
    }

    public ObservableCollection<string> SearchRoots { get; } = new();
    public ObservableCollection<LargeFileEntry> Files { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Wähle Ordner und starte die Suche nach großen Dateien.";

    [RelayCommand]
    public async Task FindAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        Files.Clear();

        var min = _settings.LargeFileThresholdMb * 1024L * 1024L;
        var progress = new Progress<ScanProgress>(p =>
            StatusText = $"Scanne... {p.FilesSoFar} Dateien geprüft");
        try
        {
            var result = await _finder.FindAsync(SearchRoots.ToList(), min, 500, progress, _cts.Token);
            foreach (var f in result) Files.Add(f);
            StatusText = $"{result.Count} Datei(en) gefunden, größer als {_settings.LargeFileThresholdMb} MB.";
        }
        catch (OperationCanceledException) { StatusText = "Abgebrochen."; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void Reveal(LargeFileEntry? entry)
    {
        if (entry is null) return;
        try { Process.Start("explorer.exe", $"/select,\"{entry.Path}\""); } catch { /* ignore */ }
    }

    [RelayCommand]
    public void Delete(LargeFileEntry? entry)
    {
        if (entry is null) return;
        var msg = _settings.UseRecycleBin
            ? $"In den Papierkorb verschieben?\n\n{entry.Path}"
            : $"ENDGÜLTIG löschen?\n\n{entry.Path}";
        if (MessageBox.Show(msg, "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (_fs.DeleteFile(entry.Path, _settings.UseRecycleBin))
            Files.Remove(entry);
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
