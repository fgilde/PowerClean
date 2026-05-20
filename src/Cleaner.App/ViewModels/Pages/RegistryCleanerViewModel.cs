using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class RegistryCleanerViewModel : ObservableObject
{
    private readonly IRegistryScanner _scanner;

    public RegistryCleanerViewModel(IRegistryScanner scanner)
    {
        _scanner = scanner;
        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegistryIssue.Category)));
    }

    public ObservableCollection<RegistryIssue> Issues { get; } = new();
    public ICollectionView IssuesView { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Scan startet Suche nach veralteten Einträgen in der Registry. Vor jedem Löschen wird automatisch ein .reg-Backup erstellt.";

    [ObservableProperty]
    private int _selectedCount;

    [RelayCommand]
    public async Task ScanAsync()
    {
        IsBusy = true;
        Issues.Clear();
        try
        {
            var progress = new Progress<string>(s => StatusText = "Scanne: " + s);
            var result = await _scanner.ScanAsync(progress);
            foreach (var i in result)
            {
                // Default-Auswahl: Safe-Level vorausgewählt
                i.IsSelected = i.Safety == IssueSafety.Safe;
                Issues.Add(i);
            }
            RecomputeSelected();
            StatusText = $"{Issues.Count} Probleme gefunden " +
                         $"({Issues.Count(i => i.Safety == IssueSafety.Safe)} sicher, " +
                         $"{Issues.Count(i => i.Safety == IssueSafety.Caution)} mit Vorsicht).";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RegistryScan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    public async Task CleanAsync()
    {
        var selected = Issues.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var msg = $"{selected.Count} Registry-Einträge werden gelöscht.\n\n" +
                  "Ein .reg-Backup wird automatisch erstellt und kann per Doppelklick wieder importiert werden.\n\n" +
                  "Backup-Speicherort: %LOCALAPPDATA%\\Cleaner\\RegistryBackups\\\n\n" +
                  "Fortfahren?";
        if (MessageBox.Show(msg, "Registry-Bereinigung bestätigen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _scanner.CleanAsync(selected);

            foreach (var i in selected) Issues.Remove(i);

            StatusText = $"{result.Deleted} gelöscht, {result.Failed} fehlgeschlagen. Backup: {result.BackupFilePath}";

            if (MessageBox.Show($"Fertig: {result.Deleted} Einträge entfernt.\n\n" +
                                $"Backup wurde unter:\n{result.BackupFilePath}\n\n" +
                                "Backup-Ordner jetzt öffnen?",
                    "Bereinigung abgeschlossen",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Cleaner.App.Helpers.PathOpener.RevealInExplorer(result.BackupFilePath);
            }
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RegistryClean", ex);
            StatusText = "Fehler beim Bereinigen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RecomputeSelected();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanClean() => !IsBusy && Issues.Any(i => i.IsSelected);

    public void RecomputeSelected()
    {
        SelectedCount = Issues.Count(i => i.IsSelected);
        CleanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void SelectSafe() { foreach (var i in Issues) i.IsSelected = i.Safety == IssueSafety.Safe; Refresh(); }

    [RelayCommand]
    public void SelectAll() { foreach (var i in Issues) i.IsSelected = true; Refresh(); }

    [RelayCommand]
    public void SelectNone() { foreach (var i in Issues) i.IsSelected = false; Refresh(); }

    [RelayCommand]
    public void OpenBackupFolder()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cleaner", "RegistryBackups");
        System.IO.Directory.CreateDirectory(dir);
        Cleaner.App.Helpers.PathOpener.OpenContainingFolder(dir);
    }

    private void Refresh()
    {
        // Erzwinge UI-Refresh, weil RegistryIssue.IsSelected kein INPC ist
        var snapshot = Issues.ToList();
        Issues.Clear();
        foreach (var i in snapshot) Issues.Add(i);
        RecomputeSelected();
    }
}
