using System.Collections.ObjectModel;
using System.Windows;
using Cleaner.Core.Cleaners;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDriveInfoService _driveInfo;
    private readonly ICleanerRegistry _registry;
    private readonly AppSettings _settings;
    private readonly Cleaner.App.Services.RunningTaskRegistry _taskRegistry;
    private readonly Cleaner.App.Services.CleanupHistoryService _history;
    private CancellationTokenSource? _cts;

    public DashboardViewModel(IDriveInfoService driveInfo, ICleanerRegistry registry, AppSettings settings,
        Cleaner.App.Services.RunningTaskRegistry taskRegistry, Cleaner.App.Services.CleanupHistoryService history)
    {
        _driveInfo = driveInfo;
        _registry = registry;
        _settings = settings;
        _taskRegistry = taskRegistry;
        _history = history;
        Cleaner.App.Localization.L.Current.LanguageChanged += (_, _) => Greeting = BuildGreeting();
        _history.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(RefreshStats);
        Refresh();
    }

    public ObservableCollection<DriveSummary> Drives { get; } = new();
    public ObservableCollection<QuickCleanItem> SuggestedCleanups { get; } = new();

    [ObservableProperty]
    private string _greeting = string.Empty;

    [ObservableProperty]
    private long _potentialSavings;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    private string _scanStatus = string.Empty;

    [ObservableProperty]
    private long _freedAllTime;

    [ObservableProperty]
    private long _freedThisMonth;

    [ObservableProperty]
    private int _healthScore;

    [ObservableProperty]
    private string _healthLabel = string.Empty;

    public bool IsBusy => IsScanning || IsCleaning;

    partial void OnIsScanningChanged(bool value) { OnPropertyChanged(nameof(IsBusy)); }
    partial void OnIsCleaningChanged(bool value) { OnPropertyChanged(nameof(IsBusy)); }

    [RelayCommand]
    public void Refresh()
    {
        Drives.Clear();
        foreach (var d in _driveInfo.EnumerateDrives())
            Drives.Add(d);

        Greeting = BuildGreeting();

        SuggestedCleanups.Clear();
        foreach (var c in _registry.All.Where(x => x.SafetyLevel <= SafetyLevel.Recommended && x.IsAvailable()))
            SuggestedCleanups.Add(new QuickCleanItem(c));

        RefreshStats();
    }

    /// <summary>Aktualisiert die Insights-Kacheln (Freigegeben gesamt/Monat, Health-Score).</summary>
    private void RefreshStats()
    {
        FreedAllTime = _history.TotalFreedAllTime;

        var now = DateTime.Now;
        var monthStart = new DateTimeOffset(new DateTime(now.Year, now.Month, 1));
        FreedThisMonth = _history.Entries.Where(e => e.CleanedAt >= monthStart).Sum(e => e.FreedBytes);

        // Health-Score = durchschnittlicher freier Speicher über alle Laufwerke (0–100).
        var drives = Drives.ToList();
        double freeFraction = drives.Count == 0
            ? 1.0
            : drives.Average(d => d.TotalSize <= 0 ? 1.0 : (double)d.FreeSpace / d.TotalSize);

        HealthScore = Math.Clamp((int)Math.Round(freeFraction * 100), 0, 100);
        var key = HealthScore >= 50 ? "Dashboard.Health.Good"
                : HealthScore >= 20 ? "Dashboard.Health.Ok"
                : "Dashboard.Health.Low";
        HealthLabel = Cleaner.App.Localization.L.Current[key];
    }

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    public async Task QuickScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        PotentialSavings = 0;
        ScanProgress = 0;

        int total = SuggestedCleanups.Count;
        int done = 0;
        long sum = 0;
        object gate = new();

        var bgTask = _taskRegistry.Start(
            title: "Quick-Scan", category: "Scan", cts: _cts);

        try
        {
            // Bis zu 4 Cleaner parallel — mehr thrasht die Disk.
            await Parallel.ForEachAsync(
                SuggestedCleanups,
                new ParallelOptions
                {
                    CancellationToken = _cts.Token,
                    MaxDegreeOfParallelism = 4,
                },
                async (item, ct) =>
                {
                    item.IsScanning = true;
                    item.HasResult = false;
                    try
                    {
                        var r = await item.Target.ScanAsync(null, ct);
                        item.Size = r.SizeBytes;
                        item.Files = r.FileCount;
                        item.LastScan = r;
                        item.HasResult = true;

                        lock (gate)
                        {
                            sum += r.SizeBytes;
                            done++;
                            PotentialSavings = sum;
                            ScanProgress = total == 0 ? 0 : (double)done / total * 100;
                            ScanStatus = $"Scanne {done}/{total} — bisher " +
                                         Cleaner.Core.Utils.ByteFormatter.Format(sum);
                            bgTask.Progress = ScanProgress;
                            bgTask.StatusText = ScanStatus;
                            bgTask.BytesProcessed = sum;
                            bgTask.ItemsProcessed = done;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        item.StatusMessage = "Fehler: " + ex.Message;
                    }
                    finally
                    {
                        item.IsScanning = false;
                    }
                });

            ScanStatus = $"Fertig — {Cleaner.Core.Utils.ByteFormatter.Format(sum)} aufräumbar.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan abgebrochen.";
        }
        finally
        {
            _taskRegistry.Complete(bgTask);
            IsScanning = false;
            ScanProgress = 0;
            QuickCleanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanQuickClean))]
    public async Task QuickCleanAsync(string? mode = null)
    {
        var toClean = SuggestedCleanups.Where(i => i.HasResult && i.Size > 0).ToList();
        if (toClean.Count == 0) return;

        bool useRecycleBin = mode switch
        {
            "recycle" => true,
            "permanent" => false,
            _ => _settings.UseRecycleBin,
        };

        var totalSize = toClean.Sum(i => i.Size);
        var msg = $"{toClean.Count} Kategorie(n) mit zusammen " +
                  $"{Cleaner.Core.Utils.ByteFormatter.Format(totalSize)} aufräumen?\n\n" +
                  (useRecycleBin
                      ? "Dateien gehen in den Papierkorb."
                      : "ACHTUNG: Endgültiges Löschen.");

        if (MessageBox.Show(msg, "Aufräumen", MessageBoxButton.YesNo,
                useRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsCleaning = true;
        long freedTotal = 0;
        int done = 0;
        int total = toClean.Count;

        var bgTask = _taskRegistry.Start(
            title: "Quick-Cleanup",
            category: useRecycleBin ? "Papierkorb" : "Endgültig löschen",
            cts: _cts);

        try
        {
            foreach (var item in toClean)
            {
                if (_cts.IsCancellationRequested) break;
                item.IsCleaning = true;
                try
                {
                    var cleanedPaths = item.LastScan!.Paths;
                    var result = await item.Target.CleanAsync(item.LastScan!, useRecycleBin, null, _cts.Token);
                    _history.Record(result, item.Name, useRecycleBin, cleanedPaths);
                    freedTotal += result.FreedBytes;
                    item.Freed = result.FreedBytes;
                    item.Size = 0;
                    item.HasResult = false;
                    item.StatusMessage = $"{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytes)} freigegeben";
                }
                catch (Exception ex)
                {
                    item.StatusMessage = "Fehler: " + ex.Message;
                }
                finally
                {
                    item.IsCleaning = false;
                    done++;
                    ScanProgress = total == 0 ? 0 : (double)done / total * 100;
                    ScanStatus = $"Aufräumen {done}/{total} — bisher freigegeben " +
                                 Cleaner.Core.Utils.ByteFormatter.Format(freedTotal);
                    bgTask.Progress = ScanProgress;
                    bgTask.StatusText = ScanStatus;
                    bgTask.BytesProcessed = freedTotal;
                    bgTask.ItemsProcessed = done;
                }
            }
            PotentialSavings = SuggestedCleanups.Where(i => i.HasResult).Sum(i => i.Size);
            ScanStatus = $"Fertig — {Cleaner.Core.Utils.ByteFormatter.Format(freedTotal)} freigegeben.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Aufräumen abgebrochen.";
        }
        finally
        {
            _taskRegistry.Complete(bgTask);
            IsCleaning = false;
            ScanProgress = 0;
            Refresh(); // Drives neu laden für freigegebenen Platz
        }
    }

    private bool CanQuickClean() => !IsBusy && SuggestedCleanups.Any(i => i.HasResult && i.Size > 0);

    [RelayCommand]
    public Task CleanOneAsync(QuickCleanItem? item) => CleanOneWithModeAsync(item, null);

    [RelayCommand]
    public Task CleanOneRecycleAsync(QuickCleanItem? item) => CleanOneWithModeAsync(item, "recycle");

    [RelayCommand]
    public Task CleanOnePermanentAsync(QuickCleanItem? item) => CleanOneWithModeAsync(item, "permanent");

    private async Task CleanOneWithModeAsync(QuickCleanItem? item, string? mode)
    {
        if (item is null || !item.HasResult || item.LastScan is null || item.Size == 0) return;

        bool useRecycleBin = mode switch
        {
            "recycle" => true,
            "permanent" => false,
            _ => _settings.UseRecycleBin,
        };

        var msg = $"{item.Name} aufräumen ({Cleaner.Core.Utils.ByteFormatter.Format(item.Size)})?\n\n" +
                  (useRecycleBin ? "Geht in den Papierkorb." : "ACHTUNG: Endgültiges Löschen.");
        if (MessageBox.Show(msg, "Aufräumen", MessageBoxButton.YesNo,
                useRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var cts = new CancellationTokenSource();
        item.IsCleaning = true;
        var bgTask = _taskRegistry.Start(
            title: $"Aufräumen: {item.Name}",
            category: useRecycleBin ? "Papierkorb" : "Endgültig löschen",
            cts: cts,
            indeterminate: true);
        try
        {
            var cleanedPaths = item.LastScan.Paths;
            var result = await item.Target.CleanAsync(item.LastScan, useRecycleBin, null, cts.Token);
            _history.Record(result, item.Name, useRecycleBin, cleanedPaths);
            item.Freed = result.FreedBytes;
            item.Size = 0;
            item.HasResult = false;
            item.StatusMessage = $"{Cleaner.Core.Utils.ByteFormatter.Format(result.FreedBytes)} freigegeben";
            PotentialSavings = SuggestedCleanups.Where(i => i.HasResult).Sum(i => i.Size);
            QuickCleanCommand.NotifyCanExecuteChanged();
            Refresh();
        }
        catch (OperationCanceledException) { item.StatusMessage = "abgebrochen"; }
        finally
        {
            _taskRegistry.Complete(bgTask);
            item.IsCleaning = false;
            cts.Dispose();
        }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void OpenWindowsStorage() => LaunchUri("ms-settings:storagesense");

    [RelayCommand]
    public void OpenStorageRecommendations() => LaunchUri("ms-settings:storagerecommendations");

    [RelayCommand]
    public void OpenWindowsAppsList() => LaunchUri("ms-settings:appsfeatures");

    [RelayCommand]
    public void OpenDiskCleanup() => LaunchProcess("cleanmgr.exe");

    [RelayCommand]
    public void OpenTaskManager() => LaunchProcess("taskmgr.exe");

    private static void LaunchUri(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("LaunchUri:" + uri, ex);
            System.Windows.MessageBox.Show($"Konnte '{uri}' nicht öffnen:\n{ex.Message}",
                "Cleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static void LaunchProcess(string exe)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("LaunchProcess:" + exe, ex);
            System.Windows.MessageBox.Show($"Konnte '{exe}' nicht starten:\n{ex.Message}",
                "Cleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static string BuildGreeting()
    {
        var hour = DateTime.Now.Hour;
        var name = Cleaner.App.Helpers.WindowsUserHelper.GetFirstName();
        var key = hour switch
        {
            >= 5 and < 11 => "Greeting.Morning",
            >= 11 and < 14 => "Greeting.Day",
            >= 14 and < 18 => "Greeting.Hello",
            _ => "Greeting.Evening",
        };
        return Cleaner.App.Localization.L.Current.Format(key, name);
    }
}

public sealed partial class QuickCleanItem : ObservableObject
{
    public QuickCleanItem(ICleanupTarget target) { Target = target; }
    public ICleanupTarget Target { get; }
    public string Name => Target.Name;
    public string Description => Target.Description;
    public SafetyLevel SafetyLevel => Target.SafetyLevel;

    public const int MaxVisiblePaths = 200;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private int _files;

    [ObservableProperty]
    private long _freed;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ScanResult? _lastScan;

    public IReadOnlyList<string> VisiblePaths
        => LastScan is null ? Array.Empty<string>() : LastScan.Paths.Take(MaxVisiblePaths).ToList();

    public int HiddenPathCount => (LastScan?.Paths.Count ?? 0) - VisiblePaths.Count;

    partial void OnLastScanChanged(ScanResult? value)
    {
        OnPropertyChanged(nameof(VisiblePaths));
        OnPropertyChanged(nameof(HiddenPathCount));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Cleaner.App.Helpers.PathOpener.RevealInExplorer(path);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CopyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(path);
    }
}
