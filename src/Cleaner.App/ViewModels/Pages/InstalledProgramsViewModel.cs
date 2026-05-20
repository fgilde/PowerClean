using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class InstalledProgramsViewModel : ObservableObject
{
    private readonly IInstalledProgramsScanner _scanner;
    private List<InstalledProgram> _all = new();

    public InstalledProgramsViewModel(IInstalledProgramsScanner scanner)
    {
        _scanner = scanner;
        ProgramsView = CollectionViewSource.GetDefaultView(Programs);
    }

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public ICollectionView ProgramsView { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private string _sortMode = "Größe";

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnSortModeChanged(string value) => ApplySort();

    [RelayCommand]
    public async Task ScanAsync()
    {
        IsLoading = true;
        Programs.Clear();
        try
        {
            _all = (await _scanner.ScanAsync()).ToList();
            ApplyFilter();
            TotalSize = _all.Sum(p => p.EstimatedSizeBytes);
            StatusText = $"{_all.Count} Programme · " +
                         $"{Cleaner.Core.Utils.ByteFormatter.Format(TotalSize)} gesamt";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("InstalledPrograms.Scan", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally { IsLoading = false; }
    }

    private void ApplyFilter()
    {
        Programs.Clear();
        var filtered = string.IsNullOrWhiteSpace(Filter)
            ? _all
            : _all.Where(p =>
                p.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                (p.Publisher?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        foreach (var p in filtered) Programs.Add(p);
        ApplySort();
    }

    private void ApplySort()
    {
        ProgramsView.SortDescriptions.Clear();
        var (prop, direction) = SortMode switch
        {
            "Name"      => (nameof(InstalledProgram.DisplayName),       ListSortDirection.Ascending),
            "Größe"     => (nameof(InstalledProgram.EstimatedSizeBytes), ListSortDirection.Descending),
            "Datum"     => (nameof(InstalledProgram.InstallDate),       ListSortDirection.Descending),
            "Herausgeber" => (nameof(InstalledProgram.Publisher),        ListSortDirection.Ascending),
            _ => (nameof(InstalledProgram.DisplayName), ListSortDirection.Ascending),
        };
        ProgramsView.SortDescriptions.Add(new SortDescription(prop, direction));
    }

    [RelayCommand]
    public void Uninstall(InstalledProgram? prog)
    {
        if (prog is null) return;
        if (string.IsNullOrWhiteSpace(prog.UninstallString))
        {
            MessageBox.Show($"Kein Uninstall-Eintrag für '{prog.DisplayName}'.",
                "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var hasQuiet = !string.IsNullOrWhiteSpace(prog.QuietUninstallString);
        var msg = $"'{prog.DisplayName}' deinstallieren?\n\n" +
                  (hasQuiet ? "Es ist ein Silent-Uninstall verfügbar."
                            : "Der Hersteller-Uninstaller wird gestartet.");
        if (MessageBox.Show(msg, "Deinstallieren", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!_scanner.Uninstall(prog, quiet: hasQuiet))
            MessageBox.Show("Uninstall konnte nicht gestartet werden.",
                "Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    public void ShowFolder(InstalledProgram? prog)
    {
        if (prog is null) return;

        // Versuche der Reihe nach: InstallLocation → DisplayIcon-Parent → UninstallString-Parent
        var candidates = new[]
        {
            prog.InstallLocation,
            prog.IconPath is not null ? System.IO.Path.GetDirectoryName(Cleaner.App.Helpers.PathOpener.Resolve(prog.IconPath) ?? "") : null,
            prog.UninstallString is not null ? System.IO.Path.GetDirectoryName(Cleaner.App.Helpers.PathOpener.Resolve(prog.UninstallString) ?? "") : null,
            prog.QuietUninstallString is not null ? System.IO.Path.GetDirectoryName(Cleaner.App.Helpers.PathOpener.Resolve(prog.QuietUninstallString) ?? "") : null,
        };

        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (Cleaner.App.Helpers.PathOpener.RevealInExplorer(c)) return;
        }

        MessageBox.Show($"Kein Ordner für '{prog.DisplayName}' gefunden.\n\n" +
                        $"InstallLocation: {prog.InstallLocation ?? "—"}\n" +
                        $"DisplayIcon: {prog.IconPath ?? "—"}\n" +
                        $"UninstallString: {prog.UninstallString ?? "—"}",
            "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public void CopyName(InstalledProgram? prog)
    {
        if (prog is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(prog.DisplayName);
    }

    [RelayCommand]
    public void SearchWeb(InstalledProgram? prog)
    {
        if (prog is null) return;
        var query = System.Net.WebUtility.UrlEncode(prog.DisplayName + " " + (prog.Publisher ?? ""));
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"https://www.google.com/search?q={query}") { UseShellExecute = true });
        }
        catch (Exception ex) { Cleaner.App.App.LogException("SearchWeb", ex); }
    }

    [RelayCommand]
    public void OpenRegistryKey(InstalledProgram? prog)
    {
        if (prog is null) return;
        try
        {
            // Setze LastKey damit regedit direkt dorthin springt
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            key.SetValue("LastKey", prog.RegistryKey);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("regedit.exe")
            { UseShellExecute = true });
        }
        catch (Exception ex) { Cleaner.App.App.LogException("OpenRegistryKey", ex); }
    }
}
