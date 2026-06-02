using System.Collections.ObjectModel;
using System.Windows;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class CleanupHistoryViewModel : ObservableObject
{
    private readonly CleanupHistoryService _history;
    private readonly RecycleBinService _recycleBin;

    public CleanupHistoryViewModel(CleanupHistoryService history, RecycleBinService recycleBin)
    {
        _history = history;
        _recycleBin = recycleBin;
        _history.Changed += (_, _) => Marshal(Reload);
        Reload();
    }

    public ObservableCollection<CleanupHistoryEntry> Entries { get; } = new();

    [ObservableProperty]
    private long _totalFreedAllTime;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = string.Empty;

    private void Reload()
    {
        Entries.Clear();
        foreach (var e in _history.Entries)
            Entries.Add(e);
        TotalFreedAllTime = _history.TotalFreedAllTime;
        IsEmpty = Entries.Count == 0;
    }

    [RelayCommand]
    private void Restore(CleanupHistoryEntry? entry)
    {
        if (entry is null) return;
        if (!entry.UsedRecycleBin)
        {
            MessageBox.Show(
                "Dieser Vorgang hat endgültig gelöscht — eine Wiederherstellung ist nicht möglich.",
                "Wiederherstellen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText = "Stelle wieder her...";
        int restored = _recycleBin.RestoreByOriginalPaths(entry.Paths);
        StatusText = restored > 0
            ? $"{restored} Datei(en) aus dem Papierkorb wiederhergestellt."
            : "Nichts wiederhergestellt — die Dateien sind evtl. nicht mehr im Papierkorb.";

        if (restored > 0)
            MessageBox.Show(StatusText, "Wiederherstellen", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(
                StatusText + "\n\nDu kannst den Papierkorb auch manuell öffnen.",
                "Wiederherstellen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void OpenRecycleBin() => RecycleBinService.OpenRecycleBin();

    [RelayCommand]
    private void Remove(CleanupHistoryEntry? entry)
    {
        if (entry is null) return;
        _history.Remove(entry.Id);
    }

    [RelayCommand]
    private void Clear()
    {
        if (Entries.Count == 0) return;
        if (MessageBox.Show("Gesamten Verlauf löschen?", "Verlauf leeren",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _history.Clear();
    }

    private static void Marshal(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }
        app.Dispatcher.Invoke(action);
    }
}
