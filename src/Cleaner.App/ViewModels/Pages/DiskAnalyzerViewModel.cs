using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Cleaner.App.ViewModels.Items;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels.Pages;

public sealed partial class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskScanner _scanner;
    private readonly IDriveInfoService _driveInfo;
    private readonly IFileSystemOperations _fs;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _cts;

    public DiskAnalyzerViewModel(IDiskScanner scanner, IDriveInfoService driveInfo,
        IFileSystemOperations fs, AppSettings settings)
    {
        _scanner = scanner;
        _driveInfo = driveInfo;
        _fs = fs;
        _settings = settings;

        foreach (var d in _driveInfo.EnumerateDrives())
            AvailableDrives.Add(d);
        SelectedDrive = AvailableDrives.FirstOrDefault();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _refreshTimer.Tick += (_, _) =>
        {
            try { RefreshLiveView(); }
            catch (Exception ex)
            {
                Cleaner.App.App.LogException("RefreshTick", ex);
                StatusText = "Live-Update-Fehler: " + ex.Message;
            }
        };
    }

    /// <summary>Wird bei jedem Live-Tick gefeuert — Page nutzt das zum Treemap-Refresh.</summary>
    public event EventHandler? TreeUpdated;

    public ObservableCollection<DriveSummary> AvailableDrives { get; } = new();

    /// <summary>Wurzel-VMs für den Tree-View. Wird bei Drilldown / Scan neu gesetzt.</summary>
    public ObservableCollection<FileSystemNodeViewModel> TreeRoots { get; } = new();

    public ObservableCollection<FileSystemNode> Breadcrumb { get; } = new();

    [ObservableProperty]
    private DriveSummary? _selectedDrive;

    [ObservableProperty]
    private string? _customPath;

    [ObservableProperty]
    private FileSystemNode? _rootNode;

    [ObservableProperty]
    private FileSystemNode? _currentNode;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Wähle ein Laufwerk und klicke auf 'Scan starten'.";

    [ObservableProperty]
    private long _bytesScanned;

    [ObservableProperty]
    private int _filesScanned;

    [ObservableProperty]
    private long _currentNodeSize;

    [ObservableProperty]
    private int _currentNodeFileCount;

    [RelayCommand]
    public void BrowseFolder()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Ordner für Disk-Analyse auswählen",
                InitialDirectory = !string.IsNullOrWhiteSpace(CustomPath) && Directory.Exists(CustomPath)
                    ? CustomPath
                    : SelectedDrive?.RootPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                CustomPath = dlg.FolderName;
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("BrowseFolder", ex);
        }
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        var path = !string.IsNullOrWhiteSpace(CustomPath) ? CustomPath : SelectedDrive?.RootPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            StatusText = "Pfad nicht gefunden.";
            return;
        }

        Cleaner.App.App.LogInfo($"Disk-Scan startet: {path}");
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsScanning = true;
        BytesScanned = 0;
        FilesScanned = 0;
        StatusText = $"Scanne {path} ...";

        var progress = new Progress<ScanProgress>(p =>
        {
            BytesScanned = p.BytesSoFar;
            FilesScanned = p.FilesSoFar;
        });

        // Tree-Wurzel kommt SOFORT zurück, Befüllung läuft im Hintergrund
        var session = _scanner.StartScan(path, progress, _cts.Token);
        RootNode = session.Root;
        SetCurrent(session.Root);
        _refreshTimer.Start();

        try
        {
            await session.Completion;
            StatusText = $"Scan fertig: {Cleaner.Core.Utils.ByteFormatter.Format(session.Root.Size)} in {session.Root.FileCount} Dateien.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan abgebrochen — bisheriger Stand wird angezeigt.";
        }
        catch (AggregateException agg) when (agg.InnerExceptions.All(x => x is OperationCanceledException))
        {
            StatusText = "Scan abgebrochen.";
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("DiskScan.Completion", ex);
            StatusText = "Scan-Fehler: " + ex.Message;
        }
        finally
        {
            _refreshTimer.Stop();
            IsScanning = false;
            try { RefreshLiveView(); } catch (Exception ex) { Cleaner.App.App.LogException("FinalRefresh", ex); }
            Cleaner.App.App.LogInfo($"Disk-Scan beendet — {session.Root.FileCount} Dateien, {session.Root.Size} bytes");
        }
    }

    private void RefreshLiveView()
    {
        if (CurrentNode is null) return;

        CurrentNodeSize = CurrentNode.Size;
        CurrentNodeFileCount = CurrentNode.FileCount;

        // Tree-Roots: VMs für die Children des CurrentNode, mit Live-Sync
        var existingNodes = TreeRoots.Select(r => r.Node).ToHashSet();
        var snapshot = CurrentNode.ChildrenSnapshot();
        foreach (var n in snapshot)
        {
            if (!existingNodes.Contains(n))
                TreeRoots.Add(new FileSystemNodeViewModel(n));
        }

        // Refresh expandierte Children (nur die sichtbaren VMs)
        foreach (var r in TreeRoots) r.Refresh();

        // Sortierung beibehalten — größte Einträge oben
        SortObservableByDescendingSize(TreeRoots);

        TreeUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sortiert eine ObservableCollection in-place per Move-Operationen (Selection-Sort).
    /// Bewahrt das Container/Selection-State, anders als Clear+Add.
    /// </summary>
    internal static void SortObservableByDescendingSize(ObservableCollection<FileSystemNodeViewModel> col)
    {
        for (int i = 0; i < col.Count - 1; i++)
        {
            int maxIdx = i;
            for (int j = i + 1; j < col.Count; j++)
            {
                if (col[j].Size > col[maxIdx].Size) maxIdx = j;
            }
            if (maxIdx != i) col.Move(maxIdx, i);
        }
    }

    [RelayCommand]
    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void DrillInto(FileSystemNode? node)
    {
        if (node is null || !node.IsDirectory) return;
        SetCurrent(node);
        RefreshLiveView();
    }

    [RelayCommand]
    public void DrillIntoVm(FileSystemNodeViewModel? vm)
    {
        if (vm is null || vm.IsDummy || !vm.IsDirectory) return;
        DrillInto(vm.Node);
    }

    // ---- File operations / context menu commands ----

    [RelayCommand]
    public void OpenInExplorer(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        Cleaner.App.Helpers.PathOpener.RevealInExplorer(path);
    }

    [RelayCommand]
    public void OpenWith(object? param)
    {
        var path = ResolvePath(param);
        if (path is null || !File.Exists(path)) return;
        Cleaner.App.Helpers.PathOpener.OpenDefault(path);
    }

    [RelayCommand]
    public void OpenWithDialog(object? param)
    {
        var path = ResolvePath(param);
        if (path is null || !File.Exists(path)) return;
        Cleaner.App.Helpers.PathOpener.OpenWithDialog(path);
    }

    [RelayCommand]
    public void ShowProperties(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        Cleaner.App.Helpers.PathOpener.ShowProperties(path);
    }

    [RelayCommand]
    public void CopyPath(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        Cleaner.App.Helpers.PathOpener.CopyToClipboard(path);
    }

    [RelayCommand]
    public void OpenSystemContextMenu(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        var owner = Application.Current?.MainWindow;
        if (owner is null) return;
        Cleaner.App.Helpers.ShellContextMenu.ShowFor(owner, path, extendedVerbs: false);
    }

    [RelayCommand]
    public void OpenInTerminal(object? param)
    {
        var path = ResolvePath(param);
        if (path is null) return;
        var dir = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
        if (dir is null) return;
        Cleaner.App.Helpers.TerminalLauncher.OpenIn(dir);
    }

    [RelayCommand]
    public void DeleteNode(object? param)
    {
        var (path, vm) = ResolvePathAndVm(param);
        if (path is null) return;

        bool isDir = Directory.Exists(path);
        bool isFile = File.Exists(path);
        if (!isDir && !isFile) return;

        var sizeText = vm is not null ? Cleaner.Core.Utils.ByteFormatter.Format(vm.Size) : "";
        var what = isDir ? "Ordner" : "Datei";
        var msg = _settings.UseRecycleBin
            ? $"{what} in den Papierkorb verschieben?\n\n{path}\n{sizeText}"
            : $"{what} ENDGÜLTIG löschen (Papierkorb deaktiviert)?\n\n{path}\n{sizeText}";

        if (MessageBox.Show(msg, "Löschen bestätigen",
                MessageBoxButton.YesNo,
                isDir ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        bool ok = isDir
            ? _fs.DeleteDirectory(path, _settings.UseRecycleBin)
            : _fs.DeleteFile(path, _settings.UseRecycleBin);

        if (ok)
        {
            // VM aus dem Tree entfernen
            if (vm is not null) RemoveVmFromTree(vm);
            StatusText = $"Gelöscht: {path}";
        }
        else
        {
            MessageBox.Show($"Konnte nicht löschen: {path}", "Cleaner",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveVmFromTree(FileSystemNodeViewModel target)
    {
        // top-level
        if (TreeRoots.Remove(target)) return;
        // depth search via recursion (Tree depth ist begrenzt)
        foreach (var root in TreeRoots)
            if (TryRemoveDeep(root, target)) return;
    }

    private static bool TryRemoveDeep(FileSystemNodeViewModel parent, FileSystemNodeViewModel target)
    {
        if (parent.Children.Remove(target)) return true;
        foreach (var c in parent.Children.ToArray())
            if (TryRemoveDeep(c, target)) return true;
        return false;
    }

    private static string? ResolvePath(object? param) => param switch
    {
        FileSystemNodeViewModel vm when !vm.IsDummy => vm.FullPath,
        FileSystemNode n => n.FullPath,
        string s => s,
        _ => null,
    };

    private static (string? path, FileSystemNodeViewModel? vm) ResolvePathAndVm(object? param) => param switch
    {
        FileSystemNodeViewModel vm when !vm.IsDummy => (vm.FullPath, vm),
        FileSystemNode n => (n.FullPath, null),
        string s => (s, null),
        _ => (null, null),
    };

    [RelayCommand]
    public void NavigateBreadcrumb(FileSystemNode? node)
    {
        if (node is null) return;
        SetCurrent(node);
        RefreshLiveView();
    }

    [RelayCommand]
    public void GoUp()
    {
        if (CurrentNode?.Parent is { } p) { SetCurrent(p); RefreshLiveView(); }
    }

    private void SetCurrent(FileSystemNode node)
    {
        CurrentNode = node;
        Breadcrumb.Clear();
        TreeRoots.Clear();

        var stack = new Stack<FileSystemNode>();
        var n = node;
        while (n is not null)
        {
            stack.Push(n);
            n = n.Parent;
        }
        while (stack.Count > 0)
            Breadcrumb.Add(stack.Pop());
    }
}
