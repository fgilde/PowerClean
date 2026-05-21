using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Cleaner.App.Services;
using Cleaner.App.ViewModels.Items;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class LogFinderViewModel : ObservableObject
{
    private readonly ILogFinder _finder;
    private readonly IFileSystemOperations _fs;
    private readonly AppSettings _settings;
    private readonly RunningTaskRegistry _taskRegistry;
    private CancellationTokenSource? _cts;

    public LogFinderViewModel(ILogFinder finder, IFileSystemOperations fs, AppSettings settings, RunningTaskRegistry taskRegistry)
    {
        _finder = finder;
        _fs = fs;
        _settings = settings;
        _taskRegistry = taskRegistry;

        // Selected-Size live mit-tracken: bei Add/Remove an Items koppeln, bei
        // IsSelected-Changes auf jedem Item neu summieren.
        Files.CollectionChanged += OnFilesChanged;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (LogFileItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (LogFileItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;
        RecomputeSelection();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogFileItemViewModel.IsSelected))
            RecomputeSelection();
    }

    private void RecomputeSelection()
    {
        long size = 0;
        int count = 0;
        foreach (var f in Files)
        {
            if (f.IsSelected) { size += f.Size; count++; }
        }
        SelectedSize = size;
        SelectedCount = count;
    }

    /// <summary>Extra-Suchpfade die der User dazugeschoben hat. Default-Roots sind in LogFinder hardcoded.</summary>
    public ObservableCollection<string> ExtraRoots { get; } = new();

    public ObservableCollection<LogFileItemViewModel> Files { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Klicke 'Scan starten' um Log- und Temp-Dateien zu finden.";

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _selectedSize;

    [ObservableProperty]
    private int _selectedCount;

    [RelayCommand]
    public async Task FindAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        Files.Clear();
        TotalSize = 0;
        Count = 0;
        StatusText = "Scanne Temp, AppData, Windows-Logs …";

        var patterns = ParsePatterns(_settings.LogFinderPatterns);
        var minAge = TimeSpan.FromDays(Math.Max(0, _settings.LogFinderMinAgeDays));
        var options = new LogFinderOptions
        {
            Patterns = patterns,
            MinAge = minAge,
            ExtraRoots = ExtraRoots.ToList(),
        };

        // Als Background-Task registrieren — sichtbar im Dashboard-Status-Strip.
        var bgTask = _taskRegistry.Start(
            title: "Log-Finder: Scan",
            category: "Scan",
            cts: _cts,
            indeterminate: true);

        var progress = new Progress<ScanProgress>(p =>
        {
            StatusText = $"Scanne... {p.FilesSoFar} Dateien geprüft";
            bgTask.StatusText = $"{p.FilesSoFar} Dateien geprüft";
            bgTask.ItemsProcessed = p.FilesSoFar;
        });

        try
        {
            var result = await Task.Run(() => _finder.FindAsync(options, progress, _cts.Token), _cts.Token);
            foreach (var f in result) Files.Add(new LogFileItemViewModel(f));
            TotalSize = result.Sum(r => r.Size);
            Count = result.Count;
            StatusText = result.Count == 0
                ? "Keine passenden Dateien gefunden."
                : $"{result.Count} Datei(en) gefunden, {Cleaner.Core.Utils.ByteFormatter.Format(TotalSize)} gesamt.";
        }
        catch (OperationCanceledException) { StatusText = "Abgebrochen."; }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("LogFinderScan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            _taskRegistry.Complete(bgTask);
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var f in Files) f.IsSelected = true;
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var f in Files) f.IsSelected = false;
    }

    /// <summary>
    /// Markiert "klar disposable" Dateien: alle .tmp/.temp/.bak/.old/.dmp/.mdmp PLUS
    /// .log älter als 30 Tage. Echte aktive App-Logs (.log neuer als 30d) werden in
    /// Ruhe gelassen.
    /// </summary>
    [RelayCommand]
    public void SelectRecommended()
    {
        var recommendedExts = new HashSet<string>(
            new[] { ".tmp", ".temp", ".bak", ".old", ".dmp", ".mdmp" },
            StringComparer.OrdinalIgnoreCase);
        var logCutoff = DateTime.UtcNow.AddDays(-30);

        foreach (var f in Files)
        {
            var ext = System.IO.Path.GetExtension(f.Path);
            bool extMatch = recommendedExts.Contains(ext);
            bool oldLog = string.Equals(ext, ".log", StringComparison.OrdinalIgnoreCase)
                          && f.LastWriteUtc < logCutoff;
            f.IsSelected = extMatch || oldLog;
        }
    }

    [RelayCommand]
    public void AddSearchRoot()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner zum Durchsuchen hinzufügen" };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            ExtraRoots.Add(dlg.FolderName);
    }

    [RelayCommand]
    public void RemoveSearchRoot(string? root)
    {
        if (root is not null) ExtraRoots.Remove(root);
    }

    [RelayCommand]
    public void Reveal(LogFileItemViewModel? item)
    {
        if (item is null) return;
        try { Process.Start("explorer.exe", $"/select,\"{item.Path}\""); } catch { /* ignore */ }
    }

    /// <summary>Einzelne Datei löschen — nutzt AppSettings.UseRecycleBin als Default-Mode.</summary>
    [RelayCommand]
    public void Delete(LogFileItemViewModel? item)
    {
        if (item is null) return;
        bool useRecycle = _settings.UseRecycleBin;
        var msg = useRecycle
            ? $"In den Papierkorb verschieben?\n\n{item.Path}"
            : $"ENDGÜLTIG löschen?\n\n{item.Path}";
        if (MessageBox.Show(msg, "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (_fs.DeleteFile(item.Path, useRecycle))
            RemoveItem(item);
    }

    /// <summary>
    /// Bulk-Delete der markierten Dateien. Parameter "recycle" (Default) oder "permanent" —
    /// wird vom Dropdown-Menü auf dem Delete-Button übergeben. Läuft als Background-Task
    /// (sichtbar im Dashboard-Status-Strip).
    /// </summary>
    [RelayCommand]
    public async Task DeleteSelected(string? mode)
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool useRecycle = !string.Equals(mode, "permanent", StringComparison.OrdinalIgnoreCase);
        var totalBytes = selected.Sum(s => s.Size);
        var sizeText = Cleaner.Core.Utils.ByteFormatter.Format(totalBytes);
        var msg = useRecycle
            ? $"{selected.Count} Datei(en) ({sizeText}) in den Papierkorb verschieben?"
            : $"{selected.Count} Datei(en) ({sizeText}) ENDGÜLTIG löschen?";
        if (MessageBox.Show(msg,
                useRecycle ? "Markierte in Papierkorb" : "Markierte endgültig löschen",
                MessageBoxButton.YesNo,
                useRecycle ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var cts = new CancellationTokenSource();
        var bgTask = _taskRegistry.Start(
            title: useRecycle ? "Log-Finder: Papierkorb" : "Log-Finder: Endgültig löschen",
            category: "Clean",
            cts: cts,
            indeterminate: false);

        int deleted = 0;
        long bytesDone = 0;
        try
        {
            await Task.Run(() =>
            {
                int idx = 0;
                foreach (var item in selected)
                {
                    if (cts.IsCancellationRequested) break;
                    bool ok = _fs.DeleteFile(item.Path, useRecycle);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (ok) RemoveItem(item);
                    });
                    if (ok) { deleted++; bytesDone += item.Size; }
                    idx++;
                    bgTask.Progress = (double)idx / selected.Count * 100;
                    bgTask.ItemsProcessed = idx;
                    bgTask.BytesProcessed = bytesDone;
                    bgTask.StatusText = $"{idx}/{selected.Count} · {Cleaner.Core.Utils.ByteFormatter.Format(bytesDone)}";
                }
            }, cts.Token);
            StatusText = $"{deleted} Datei(en) gelöscht ({Cleaner.Core.Utils.ByteFormatter.Format(bytesDone)}).";
        }
        catch (OperationCanceledException) { StatusText = $"Abgebrochen — {deleted} Datei(en) waren bereits weg."; }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("LogFinder.DeleteSelected", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally { _taskRegistry.Complete(bgTask); }
    }

    private void RemoveItem(LogFileItemViewModel item)
    {
        if (Files.Remove(item))
        {
            TotalSize -= item.Size;
            Count = Files.Count;
        }
    }

    private static IReadOnlyList<string> ParsePatterns(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
