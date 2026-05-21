using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class OrphanUserDataViewModel : ObservableObject
{
    private readonly IOrphanUserDataScanner _scanner;
    private readonly IFileSystemOperations _fs;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    public OrphanUserDataViewModel(IOrphanUserDataScanner scanner, IFileSystemOperations fs, AppSettings settings)
    {
        _scanner = scanner;
        _fs = fs;
        _settings = settings;
    }

    public ObservableCollection<OrphanUserDataEntry> Entries { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Bereit. Klicke 'Scan starten' um nach verwaisten Userdaten zu suchen.";

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private int _count;

    [RelayCommand]
    public async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        Entries.Clear();
        TotalSize = 0;
        Count = 0;
        StatusText = "Scanne Roaming / Local / LocalLow …";

        try
        {
            var result = await _scanner.ScanAsync(_cts.Token);
            foreach (var e in result) Entries.Add(e);
            TotalSize = result.Sum(e => e.SizeBytes);
            Count = result.Count;
            StatusText = result.Count == 0
                ? "Keine verwaisten Ordner gefunden."
                : $"{result.Count} verwaiste Ordner gefunden.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("OrphanScan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    // ---- Context menu commands (Convention: nehmen OrphanUserDataEntry ODER string-Pfad) ----

    [RelayCommand]
    public void OpenInExplorer(object? param)
    {
        var path = ResolvePath(param);
        if (path is not null) Cleaner.App.Helpers.PathOpener.RevealInExplorer(path);
    }

    [RelayCommand]
    public void OpenWith(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        if (File.Exists(path)) Cleaner.App.Helpers.PathOpener.OpenDefault(path);
        else if (Directory.Exists(path)) Cleaner.App.Helpers.PathOpener.OpenContainingFolder(path);
    }

    [RelayCommand]
    public void OpenWithDialog(object? param)
    {
        var path = ResolvePath(param);
        if (path is null || !File.Exists(path)) return;
        Cleaner.App.Helpers.PathOpener.OpenWithDialog(path);
    }

    [RelayCommand]
    public void OpenInTerminal(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (dir is null) return;
        foreach (var shell in new[] { "wt.exe", "powershell.exe", "cmd.exe" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(shell)
                {
                    UseShellExecute = true,
                    WorkingDirectory = dir,
                });
                return;
            }
            catch { /* try next */ }
        }
    }

    [RelayCommand]
    public void CopyPath(object? param)
    {
        var path = ResolvePath(param);
        if (path is not null) Cleaner.App.Helpers.PathOpener.CopyToClipboard(path);
    }

    [RelayCommand]
    public void ShowProperties(object? param)
    {
        var path = ResolvePath(param);
        if (path is not null) Cleaner.App.Helpers.PathOpener.ShowProperties(path);
    }

    [RelayCommand]
    public void OpenSystemContextMenu(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        if (Application.Current?.MainWindow is { } win)
            Cleaner.App.Helpers.ShellContextMenu.ShowFor(win, path, extendedVerbs: false);
    }

    [RelayCommand]
    public void DeleteNode(object? param)
    {
        var (path, entry) = ResolvePathAndEntry(param);
        if (path is null || !Directory.Exists(path)) return;

        var sizeText = entry is not null
            ? " (" + Cleaner.Core.Utils.ByteFormatter.Format(entry.SizeBytes) + ")"
            : "";
        var msg = _settings.UseRecycleBin
            ? $"Ordner in den Papierkorb verschieben?\n\n{path}{sizeText}"
            : $"Ordner ENDGÜLTIG löschen (Papierkorb deaktiviert)?\n\n{path}{sizeText}";

        if (MessageBox.Show(msg, "Verwaiste Userdaten löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_fs.DeleteDirectory(path, _settings.UseRecycleBin))
        {
            if (entry is not null)
            {
                Entries.Remove(entry);
                TotalSize -= entry.SizeBytes;
                Count = Entries.Count;
            }
            StatusText = $"Gelöscht: {path}";
        }
        else
        {
            MessageBox.Show($"Konnte nicht löschen: {path}", "Cleaner",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? ResolvePath(object? param) => param switch
    {
        OrphanUserDataEntry e => e.FullPath,
        string s => s,
        _ => null,
    };

    private static (string? path, OrphanUserDataEntry? entry) ResolvePathAndEntry(object? param) => param switch
    {
        OrphanUserDataEntry e => (e.FullPath, e),
        string s => (s, null),
        _ => (null, null),
    };
}
