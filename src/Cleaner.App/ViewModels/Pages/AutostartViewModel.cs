using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class AutostartViewModel : ObservableObject
{
    private readonly IAutostartScanner _scanner;

    public AutostartViewModel(IAutostartScanner scanner)
    {
        _scanner = scanner;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.SortDescriptions.Add(new SortDescription(nameof(AutostartEntry.Source), ListSortDirection.Ascending));
    }

    public ObservableCollection<AutostartEntry> Entries { get; } = new();
    public ICollectionView EntriesView { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Klicke 'Scan starten' um alle Autostarts zu laden.";

    [RelayCommand]
    public async Task ScanAsync()
    {
        IsLoading = true;
        Entries.Clear();
        try
        {
            var result = await _scanner.ScanAsync();
            foreach (var e in result) Entries.Add(e);
            StatusText = $"{Entries.Count} Autostart-Einträge gefunden.";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("Autostart.Scan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void ToggleEnabled(AutostartEntry? entry)
    {
        if (entry is null) return;
        var newState = !entry.IsEnabled;
        if (_scanner.ToggleEnabled(entry, newState))
        {
            entry.IsEnabled = newState;
            // ObservableCollection bemerkt Property-Change nicht von selbst — refresh
            var idx = Entries.IndexOf(entry);
            if (idx >= 0) { Entries.RemoveAt(idx); Entries.Insert(idx, entry); }
        }
        else
        {
            MessageBox.Show($"Konnte Status nicht ändern: {entry.Name}\n(evtl. Admin-Rechte nötig)",
                "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void Delete(AutostartEntry? entry)
    {
        if (entry is null) return;
        if (MessageBox.Show($"Autostart-Eintrag löschen?\n\n{entry.Name}\n{entry.Command}",
                "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_scanner.Delete(entry)) Entries.Remove(entry);
        else MessageBox.Show($"Konnte nicht löschen: {entry.Name}\n(evtl. Admin-Rechte nötig)",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public void ShowLocation(AutostartEntry? entry)
    {
        if (entry is null) return;
        // Erst echten Pfad aus dem Command rauspuhlen (kann Quotes + Args enthalten),
        // dann im Explorer mit Selektion öffnen. Fallback: Pfad in die Zwischenablage.
        var resolved = Cleaner.App.Helpers.PathOpener.Resolve(entry.Command);
        if (resolved is not null && Cleaner.App.Helpers.PathOpener.RevealInExplorer(resolved)) return;

        Cleaner.App.Helpers.PathOpener.CopyToClipboard(entry.Location);
        MessageBox.Show($"Zugehöriger Pfad konnte nicht gefunden werden — Registry-Location wurde in die Zwischenablage kopiert.\n\n{entry.Location}",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public void CopyCommand(AutostartEntry? entry)
    {
        if (entry is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(entry.Command);
    }

    [RelayCommand]
    public void OpenProperties(AutostartEntry? entry)
    {
        if (entry is null) return;
        var resolved = Cleaner.App.Helpers.PathOpener.Resolve(entry.Command);
        if (resolved is null) return;
        Cleaner.App.Helpers.PathOpener.ShowProperties(resolved);
    }

    [RelayCommand]
    public void Run(AutostartEntry? entry)
    {
        if (entry is null) return;
        var resolved = Cleaner.App.Helpers.PathOpener.Resolve(entry.Command);
        if (resolved is null) return;
        Cleaner.App.Helpers.PathOpener.OpenDefault(resolved);
    }
}
