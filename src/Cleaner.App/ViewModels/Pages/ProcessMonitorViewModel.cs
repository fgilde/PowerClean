using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class ProcessMonitorViewModel : ObservableObject
{
    private readonly IProcessMonitor _monitor;
    private readonly DispatcherTimer _timer;

    public ProcessMonitorViewModel(IProcessMonitor monitor)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _timer.Tick += (_, _) => { try { RefreshOnce(); } catch (Exception ex) { Cleaner.App.App.LogException("ProcessTick", ex); } };
    }

    public ObservableCollection<ProcessSnapshot> Processes { get; } = new();

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _sortMode = "RAM";

    [ObservableProperty]
    private string _statusText = "";

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value) _timer.Start(); else _timer.Stop();
    }

    public void Activate()
    {
        RefreshOnce();
        if (AutoRefresh) _timer.Start();
    }

    public void Deactivate() => _timer.Stop();

    [RelayCommand]
    public void RefreshOnce()
    {
        var snap = _monitor.Snapshot();
        var filter = Filter;
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? snap
            : snap.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               (p.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        IEnumerable<ProcessSnapshot> sorted = SortMode switch
        {
            "CPU"  => filtered.OrderByDescending(p => p.CpuPercent),
            "Name" => filtered.OrderBy(p => p.Name),
            "PID"  => filtered.OrderBy(p => p.Pid),
            "Threads" => filtered.OrderByDescending(p => p.ThreadCount),
            _      => filtered.OrderByDescending(p => p.WorkingSetBytes),
        };

        Processes.Clear();
        foreach (var p in sorted.Take(200)) Processes.Add(p);

        var total = snap.Sum(p => p.WorkingSetBytes);
        StatusText = $"{snap.Count} Prozesse · Gesamt-RAM: " +
                     Cleaner.Core.Utils.ByteFormatter.Format(total);
    }

    [RelayCommand]
    public void Kill(ProcessSnapshot? p)
    {
        if (p is null) return;
        if (MessageBox.Show($"Prozess BEENDEN?\n\n{p.Name} (PID {p.Pid})\n\n" +
                            "Ungespeicherte Daten gehen verloren.",
                "Prozess beenden", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (_monitor.Kill(p.Pid))
            RefreshOnce();
        else
            MessageBox.Show("Konnte Prozess nicht beenden.", "Cleaner",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public void OpenLocation(ProcessSnapshot? p)
    {
        if (p is null) return;
        if (!string.IsNullOrEmpty(p.FilePath))
        {
            Cleaner.App.Helpers.PathOpener.RevealInExplorer(p.FilePath);
        }
        else
        {
            _monitor.OpenFileLocation(p.Pid);
        }
    }

    [RelayCommand]
    public void ShowProperties(ProcessSnapshot? p)
    {
        if (p?.FilePath is null) return;
        Cleaner.App.Helpers.PathOpener.ShowProperties(p.FilePath);
    }

    [RelayCommand]
    public void CopyPid(ProcessSnapshot? p)
    {
        if (p is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(p.Pid.ToString());
    }

    [RelayCommand]
    public void CopyPath(ProcessSnapshot? p)
    {
        if (p?.FilePath is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(p.FilePath);
    }

    [RelayCommand]
    public void SearchWeb(ProcessSnapshot? p)
    {
        if (p is null) return;
        var query = System.Net.WebUtility.UrlEncode(p.Name + ".exe");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"https://www.google.com/search?q={query}") { UseShellExecute = true });
        }
        catch (Exception ex) { Cleaner.App.App.LogException("ProcessSearchWeb", ex); }
    }

    partial void OnFilterChanged(string value) => RefreshOnce();
    partial void OnSortModeChanged(string value) => RefreshOnce();
}
