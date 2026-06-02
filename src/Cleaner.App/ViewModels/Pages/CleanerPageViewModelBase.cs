using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using Cleaner.App.Services;
using Cleaner.App.ViewModels.Items;
using Cleaner.Core.Cleaners;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

/// <summary>
/// Gemeinsame Logik für System- und Developer-Cleaner-Pages: Liste, Scan-All, Clean-All.
/// </summary>
public abstract partial class CleanerPageViewModelBase : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly RunningTaskRegistry _taskRegistry;
    private readonly CleanupHistoryService _history;
    private readonly ProfileService _profiles;
    private ICollectionView? _itemsView;

    protected readonly ICleanerRegistry Registry;

    private CancellationTokenSource? _cts;

    protected CleanerPageViewModelBase(ICleanerRegistry registry, AppSettings settings,
        RunningTaskRegistry taskRegistry, CleanupHistoryService history, ProfileService profiles)
    {
        Registry = registry;
        _settings = settings;
        _taskRegistry = taskRegistry;
        _history = history;
        _profiles = profiles;
        _profiles.Changed += (_, _) => ReloadProfiles();
        ReloadProfiles();
    }

    public ObservableCollection<CleanupTargetItemViewModel> Items { get; } = new();

    /// <summary>Zugriff auf die App-Einstellungen für UI-Bindings (z. B. der Alters-Filter).</summary>
    public AppSettings Settings => _settings;

    // --- Profile ----------------------------------------------------------
    public ObservableCollection<string> ProfileNames { get; } = new();

    [ObservableProperty]
    private string? _selectedProfileName;

    private void ReloadProfiles()
    {
        ProfileNames.Clear();
        foreach (var p in _profiles.Profiles)
            ProfileNames.Add(p.Name);
    }

    // --- Suche ------------------------------------------------------------
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => _itemsView?.Refresh();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Bereit. Klicke auf 'Scan starten' um zu sehen, was aufgeräumt werden kann.";

    [ObservableProperty]
    private long _totalScannedSize;

    [ObservableProperty]
    private long _totalSelectedSize;

    [ObservableProperty]
    private long _totalFreedSize;

    [ObservableProperty]
    private double _scanProgress;

    protected void LoadTargets(Func<ICleanupTarget, bool> filter)
    {
        Items.Clear();
        foreach (var t in Registry.All.Where(filter))
        {
            if (!t.IsAvailable()) continue;
            var vm = new CleanupTargetItemViewModel(t);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CleanupTargetItemViewModel.IsSelected) or nameof(CleanupTargetItemViewModel.ScannedSize))
                    RecomputeSelected();
            };
            Items.Add(vm);
        }

        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = FilterItem;

        RecomputeSelected();
    }

    private bool FilterItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (obj is not CleanupTargetItemViewModel vm) return true;
        return vm.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || vm.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RecomputeSelected()
    {
        TotalSelectedSize = Items.Where(i => i.IsSelected).Sum(i => i.ScannedSize);
    }

    /// <summary>Gesamt-"Gefunden"-Wert = Summe der noch nicht aufgeräumten Scan-Größen.</summary>
    private void RecomputeScanned()
    {
        TotalScannedSize = Items.Sum(i => i.ScannedSize);
    }

    [RelayCommand(CanExecute = nameof(CanRunScan))]
    public async Task ScanAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        long totalSize = 0;
        int done = 0;
        int total = Items.Count;
        object gate = new();

        var bgTask = _taskRegistry.Start(
            title: $"Scan: {GetType().Name.Replace("ViewModel", "")}",
            category: "Scan",
            cts: _cts);

        try
        {
            // Bis zu 4 Scanner parallel — mehr thrasht die Disk.
            await Parallel.ForEachAsync(
                Items,
                new ParallelOptions
                {
                    CancellationToken = _cts.Token,
                    MaxDegreeOfParallelism = 4,
                },
                async (item, ct) =>
                {
                    item.IsScanning = true;
                    item.StatusMessage = "Scanne...";
                    var progress = new Progress<ScanProgress>(p => item.ScannedSize = p.BytesSoFar);

                    try
                    {
                        var result = await item.Target.ScanAsync(progress, ct);
                        item.LastScan = result;
                        item.ScannedSize = result.SizeBytes;
                        item.ScannedFiles = result.FileCount;
                        item.HasScanResult = true;
                        item.StatusMessage = $"{result.FileCount} Datei(en)";

                        lock (gate)
                        {
                            totalSize += result.SizeBytes;
                            done++;
                            ScanProgress = total == 0 ? 0 : (double)done / total * 100;
                            TotalScannedSize = totalSize;
                            StatusText = $"Scanne {done}/{total} — bisher " +
                                Cleaner.Core.Utils.ByteFormatter.Format(totalSize);
                            bgTask.Progress = ScanProgress;
                            bgTask.StatusText = $"{done}/{total} · {Cleaner.Core.Utils.ByteFormatter.Format(totalSize)}";
                            bgTask.BytesProcessed = totalSize;
                            bgTask.ItemsProcessed = done;
                        }
                    }
                    catch (OperationCanceledException) { item.StatusMessage = "abgebrochen"; }
                    catch (Exception ex)
                    {
                        item.StatusMessage = $"Fehler: {ex.Message}";
                        Cleaner.App.App.LogException("ScanAll." + item.Id, ex);
                    }
                    finally
                    {
                        item.IsScanning = false;
                    }
                });

            StatusText = $"Scan abgeschlossen. Aufräumbar: {Cleaner.Core.Utils.ByteFormatter.Format(totalSize)}";
        }
        catch (OperationCanceledException) { StatusText = "Scan abgebrochen."; }
        finally
        {
            _taskRegistry.Complete(bgTask);
            IsBusy = false;
            ScanProgress = 0;
            RecomputeScanned();
            RecomputeSelected();
            CleanSelectedCommand.NotifyCanExecuteChanged();
            ScanAllCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunClean))]
    public Task CleanSelectedAsync(string? mode = null)
    {
        var selected = Items.Where(i => i.IsSelected && i.HasScanResult && i.ScannedSize > 0).ToList();
        return CleanItemsAsync(selected, mode);
    }

    /// <summary>Räumt genau eine Kategorie auf — die anderen behalten ihr Scan-Ergebnis.</summary>
    [RelayCommand]
    public Task CleanItemAsync(CleanupTargetItemViewModel? item)
    {
        if (item is null || !item.HasScanResult || item.ScannedSize <= 0) return Task.CompletedTask;
        if (IsBusy) return Task.CompletedTask;
        return CleanItemsAsync(new List<CleanupTargetItemViewModel> { item }, null);
    }

    private async Task CleanItemsAsync(List<CleanupTargetItemViewModel> selected, string? mode)
    {
        if (selected.Count == 0) return;

        // Mode entscheidet ob Papierkorb oder endgültig — Override kommt vom Split-Button
        bool useRecycleBin = mode switch
        {
            "recycle" => true,
            "permanent" => false,
            _ => _settings.UseRecycleBin,
        };

        var totalSize = selected.Sum(i => i.ScannedSize);
        var msg = $"Es werden {selected.Count} Kategorie(n) mit insgesamt " +
                  $"{Cleaner.Core.Utils.ByteFormatter.Format(totalSize)} aufgeräumt.\n\n" +
                  (useRecycleBin
                      ? "Dateien werden in den Papierkorb verschoben."
                      : "ACHTUNG: Dateien werden ENDGÜLTIG gelöscht.") +
                  "\n\nFortfahren?";

        var confirm = MessageBox.Show(msg, "Aufräumen bestätigen",
            MessageBoxButton.YesNo, useRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        long freed = 0;
        int done = 0;
        int total = selected.Count;
        object gate = new();

        var bgTask = _taskRegistry.Start(
            title: $"Aufräumen: {GetType().Name.Replace("ViewModel", "")}",
            category: useRecycleBin ? "Papierkorb" : "Endgültig löschen",
            cts: _cts);

        try
        {
            // Concurrency 2 beim Cleanen — etwas konservativer als Scannen weil
            // Recycle-Bin-Operationen via SHFileOperation seriell besser laufen.
            await Parallel.ForEachAsync(
                selected,
                new ParallelOptions
                {
                    CancellationToken = _cts.Token,
                    MaxDegreeOfParallelism = 2,
                },
                async (item, ct) =>
                {
                    if (item.LastScan is null) return;

                    item.IsCleaning = true;
                    var progress = new Progress<CleanProgress>(p => item.FreedSize = p.BytesFreed);

                    try
                    {
                        var cleanedPaths = item.LastScan.Paths;
                        var result = await item.Target.CleanAsync(
                            item.LastScan, useRecycleBin, progress, ct);

                        _history.Record(result, item.Name, useRecycleBin, cleanedPaths);

                        item.FreedSize = result.FreedBytes;
                        item.FilesDeleted = result.FilesDeleted;
                        item.ScannedSize = 0;
                        item.ScannedFiles = 0;
                        item.HasScanResult = false;
                        item.StatusMessage = $"{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytes)} freigegeben";

                        lock (gate)
                        {
                            freed += result.FreedBytes;
                            done++;
                            ScanProgress = total == 0 ? 0 : (double)done / total * 100;
                            TotalFreedSize = freed;
                            StatusText = $"Räume auf {done}/{total} — bisher freigegeben " +
                                Cleaner.Core.Utils.ByteFormatter.Format(freed);
                            bgTask.Progress = ScanProgress;
                            bgTask.StatusText = $"{done}/{total} · {Cleaner.Core.Utils.ByteFormatter.Format(freed)}";
                            bgTask.BytesProcessed = freed;
                            bgTask.ItemsProcessed = done;
                        }
                    }
                    catch (OperationCanceledException) { item.StatusMessage = "abgebrochen"; }
                    catch (Exception ex)
                    {
                        item.StatusMessage = $"Fehler: {ex.Message}";
                        Cleaner.App.App.LogException("CleanSelected." + item.Id, ex);
                    }
                    finally
                    {
                        item.IsCleaning = false;
                    }
                });

            StatusText = $"Aufräumen abgeschlossen. {Cleaner.Core.Utils.ByteFormatter.Format(freed)} freigegeben.";
        }
        catch (OperationCanceledException) { StatusText = "Aufräumen abgebrochen."; }
        finally
        {
            _taskRegistry.Complete(bgTask);
            IsBusy = false;
            ScanProgress = 0;
            RecomputeScanned();
            RecomputeSelected();
            CleanSelectedCommand.NotifyCanExecuteChanged();
            ScanAllCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunClean() => !IsBusy && Items.Any(i => i.IsSelected && i.HasScanResult);

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    // --- Profile-Commands -------------------------------------------------

    /// <summary>Wendet das Profil an: hakt genau die enthaltenen Kategorien an.</summary>
    [RelayCommand]
    private void ApplyProfile(string? name)
    {
        name ??= SelectedProfileName;
        if (string.IsNullOrWhiteSpace(name)) return;
        var profile = _profiles.Get(name);
        if (profile is null) return;

        var ids = new HashSet<string>(profile.TargetIds, StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
            item.IsSelected = ids.Contains(item.Id);

        StatusText = $"Profil '{name}' angewendet.";
    }

    /// <summary>Speichert die aktuelle Auswahl als (neues oder überschriebenes) Profil.</summary>
    [RelayCommand]
    private void SaveProfileAs()
    {
        var name = Cleaner.App.Helpers.InputDialog.Show(
            "Profil speichern", "Name des Profils:", SelectedProfileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name)) return;

        var selectedIds = Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        _profiles.Save(name, selectedIds);
        SelectedProfileName = name;
        StatusText = $"Profil '{name}' gespeichert ({selectedIds.Count} Kategorie(n)).";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        var name = SelectedProfileName;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (MessageBox.Show($"Profil '{name}' löschen?", "Profil löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _profiles.Delete(name);
        SelectedProfileName = null;
    }

    // --- Export -----------------------------------------------------------

    /// <summary>Exportiert die aktuellen Scan-Ergebnisse als CSV oder JSON.</summary>
    [RelayCommand]
    private void Export()
    {
        var scanned = Items.Where(i => i.HasScanResult).ToList();
        if (scanned.Count == 0)
        {
            MessageBox.Show("Keine Scan-Ergebnisse zum Exportieren. Bitte zuerst scannen.",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Scan-Ergebnisse exportieren",
            FileName = $"powerclean-scan-{DateTime.Now:yyyy-MM-dd-HHmm}",
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var path = dialog.FileName;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ExportJson(scanned, path);
            else
                ExportCsv(scanned, path);

            StatusText = $"Exportiert nach {path}";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("Export", ex);
            MessageBox.Show($"Export fehlgeschlagen:\n{ex.Message}", "Export",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ExportCsv(IEnumerable<CleanupTargetItemViewModel> items, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kategorie;Id;Dateien;Bytes;Pfad");
        foreach (var i in items)
        {
            var paths = i.LastScan?.Paths ?? Array.Empty<string>();
            if (paths.Count == 0)
            {
                sb.AppendLine($"{Csv(i.Name)};{Csv(i.Id)};{i.ScannedFiles};{i.ScannedSize};");
                continue;
            }
            foreach (var p in paths)
                sb.AppendLine($"{Csv(i.Name)};{Csv(i.Id)};{i.ScannedFiles};{i.ScannedSize};{Csv(p)}");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static void ExportJson(IEnumerable<CleanupTargetItemViewModel> items, string path)
    {
        var data = items.Select(i => new
        {
            i.Name,
            i.Id,
            Files = i.ScannedFiles,
            Bytes = i.ScannedSize,
            Paths = i.LastScan?.Paths ?? Array.Empty<string>(),
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Csv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
