using System.Collections.ObjectModel;
using System.Windows;
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
    protected readonly ICleanerRegistry Registry;

    private CancellationTokenSource? _cts;

    protected CleanerPageViewModelBase(ICleanerRegistry registry, AppSettings settings, RunningTaskRegistry taskRegistry)
    {
        Registry = registry;
        _settings = settings;
        _taskRegistry = taskRegistry;
    }

    public ObservableCollection<CleanupTargetItemViewModel> Items { get; } = new();

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
        RecomputeSelected();
    }

    private void RecomputeSelected()
    {
        TotalSelectedSize = Items.Where(i => i.IsSelected).Sum(i => i.ScannedSize);
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
            RecomputeSelected();
            CleanSelectedCommand.NotifyCanExecuteChanged();
            ScanAllCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunClean))]
    public async Task CleanSelectedAsync(string? mode = null)
    {
        var selected = Items.Where(i => i.IsSelected && i.HasScanResult && i.ScannedSize > 0).ToList();
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
                        var result = await item.Target.CleanAsync(
                            item.LastScan, useRecycleBin, progress, ct);

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
            RecomputeSelected();
        }
    }

    private bool CanRunClean() => !IsBusy && Items.Any(i => i.IsSelected && i.HasScanResult);

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();
}
