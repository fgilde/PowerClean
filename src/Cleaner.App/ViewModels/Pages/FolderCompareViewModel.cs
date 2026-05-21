using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class FolderCompareViewModel : ObservableObject
{
    private readonly IFolderCompareService _service;
    private CancellationTokenSource? _cts;

    public FolderCompareViewModel(IFolderCompareService service)
    {
        _service = service;

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterPredicate;
    }

    public ObservableCollection<CompareEntry> Entries { get; } = new();
    public ICollectionView EntriesView { get; }

    [ObservableProperty]
    private string? _leftPath;

    [ObservableProperty]
    private string? _rightPath;

    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _equalCount;

    [ObservableProperty]
    private int _differentCount;

    [ObservableProperty]
    private int _leftOnlyCount;

    [ObservableProperty]
    private int _rightOnlyCount;

    [ObservableProperty]
    private bool _showEqual;

    [ObservableProperty]
    private bool _showDifferent = true;

    [ObservableProperty]
    private bool _showLeftOnly = true;

    [ObservableProperty]
    private bool _showRightOnly = true;

    partial void OnShowEqualChanged(bool value) => EntriesView.Refresh();
    partial void OnShowDifferentChanged(bool value) => EntriesView.Refresh();
    partial void OnShowLeftOnlyChanged(bool value) => EntriesView.Refresh();
    partial void OnShowRightOnlyChanged(bool value) => EntriesView.Refresh();

    private bool FilterPredicate(object obj)
    {
        if (obj is not CompareEntry e) return false;
        return e.Status switch
        {
            CompareStatus.Equal => ShowEqual,
            CompareStatus.Different => ShowDifferent,
            CompareStatus.LeftOnly => ShowLeftOnly,
            CompareStatus.RightOnly => ShowRightOnly,
            _ => true,
        };
    }

    [RelayCommand]
    public void BrowseLeft()
    {
        var path = PickFolder(LeftPath, "Linken Ordner wählen");
        if (path is not null) LeftPath = path;
    }

    [RelayCommand]
    public void BrowseRight()
    {
        var path = PickFolder(RightPath, "Rechten Ordner wählen");
        if (path is not null) RightPath = path;
    }

    private static string? PickFolder(string? current, string title)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title,
                InitialDirectory = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
                    ? current
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                return dlg.FolderName;
        }
        catch (Exception ex) { App.LogException("FolderCompare.PickFolder", ex); }
        return null;
    }

    [RelayCommand]
    public async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || !Directory.Exists(LeftPath))
        {
            StatusText = "Linker Ordner ungültig.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RightPath) || !Directory.Exists(RightPath))
        {
            StatusText = "Rechter Ordner ungültig.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Entries.Clear();
        EqualCount = DifferentCount = LeftOnlyCount = RightOnlyCount = 0;
        IsComparing = true;
        StatusText = "Vergleiche Ordner...";

        var progress = new Progress<CompareProgress>(p =>
            StatusText = string.IsNullOrEmpty(p.CurrentPath)
                ? $"Verarbeitet: {p.FilesProcessed}"
                : $"Vergleiche... {p.FilesProcessed} Dateien ({Truncate(p.CurrentPath, 80)})");

        App.LogInfo($"FolderCompare: '{LeftPath}' vs '{RightPath}'");

        try
        {
            var result = await _service.CompareAsync(LeftPath, RightPath, progress, _cts.Token);
            foreach (var e in result)
            {
                Entries.Add(e);
                switch (e.Status)
                {
                    case CompareStatus.Equal: EqualCount++; break;
                    case CompareStatus.Different: DifferentCount++; break;
                    case CompareStatus.LeftOnly: LeftOnlyCount++; break;
                    case CompareStatus.RightOnly: RightOnlyCount++; break;
                }
            }
            StatusText = $"Fertig. {result.Count} Eintrag/Einträge — "
                         + $"{EqualCount} gleich, {DifferentCount} unterschiedlich, "
                         + $"{LeftOnlyCount} nur links, {RightOnlyCount} nur rechts.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Vergleich abgebrochen.";
        }
        catch (Exception ex)
        {
            App.LogException("FolderCompare.CompareAsync", ex);
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            IsComparing = false;
        }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void OpenFileDiff(CompareEntry? entry)
    {
        if (entry?.LeftFullPath is null || entry.RightFullPath is null) return;

        App.LogInfo($"FolderCompare: open diff for '{entry.RelativePath}'");

        // 1) Versuche Shell-Verb "compare" — Tools wie TortoiseGitMerge registrieren das
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = entry.LeftFullPath,
                Arguments = $"\"{entry.RightFullPath}\"",
                Verb = "compare",
                UseShellExecute = true,
            };
            Process.Start(psi);
            return;
        }
        catch (Exception ex)
        {
            App.LogInfo($"FolderCompare: shell-verb 'compare' nicht verfügbar ({ex.Message}) — Fallback");
        }

        TryFallbackDiff(entry.LeftFullPath, entry.RightFullPath);
    }

    [RelayCommand]
    public void RevealLeft(CompareEntry? entry)
    {
        if (entry?.LeftFullPath is null) return;
        Helpers.PathOpener.RevealInExplorer(entry.LeftFullPath);
    }

    [RelayCommand]
    public void RevealRight(CompareEntry? entry)
    {
        if (entry?.RightFullPath is null) return;
        Helpers.PathOpener.RevealInExplorer(entry.RightFullPath);
    }

    [RelayCommand]
    public void CopyLeftPath(CompareEntry? entry)
    {
        if (entry?.LeftFullPath is null) return;
        Helpers.PathOpener.CopyToClipboard(entry.LeftFullPath);
    }

    [RelayCommand]
    public void CopyRightPath(CompareEntry? entry)
    {
        if (entry?.RightFullPath is null) return;
        Helpers.PathOpener.CopyToClipboard(entry.RightFullPath);
    }

    private static void TryFallbackDiff(string left, string right)
    {
        // Bekannte Diff-Tools im PATH
        var attempts = new (string exe, string args)[]
        {
            ("code.exe", $"--diff \"{left}\" \"{right}\""),
            ("code.cmd", $"--diff \"{left}\" \"{right}\""),
            ("WinMergeU.exe", $"\"{left}\" \"{right}\""),
            ("TortoiseGitMerge.exe", $"/base:\"{left}\" /mine:\"{right}\""),
            ("TortoiseMerge.exe", $"/base:\"{left}\" /mine:\"{right}\""),
        };

        foreach (var (exe, args) in attempts)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true,
                });
                App.LogInfo($"FolderCompare: opened with {exe}");
                return;
            }
            catch (Exception ex)
            {
                App.LogInfo($"FolderCompare: {exe} fehlgeschlagen ({ex.Message})");
            }
        }

        // Letzter Ausweg: beide Dateien per Default-App parallel öffnen
        try
        {
            Helpers.PathOpener.OpenDefault(left);
            Helpers.PathOpener.OpenDefault(right);
            App.LogInfo("FolderCompare: beide Dateien per Default-App geöffnet");
        }
        catch (Exception ex) { App.LogException("FolderCompare.FallbackDefault", ex); }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : "..." + s[^(max - 3)..];
}
