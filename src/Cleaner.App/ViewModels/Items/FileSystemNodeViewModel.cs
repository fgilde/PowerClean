using System.Collections.ObjectModel;
using Cleaner.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels.Items;

/// <summary>
/// UI-Wrapper um einen <see cref="FileSystemNode"/>. Lädt Kinder lazy beim Expand und
/// erlaubt Live-Update der Größen-/Datei-Anzeige während eines laufenden Scans via
/// <see cref="Refresh"/>.
/// </summary>
public sealed partial class FileSystemNodeViewModel : ObservableObject
{
    public FileSystemNode Node { get; }
    public bool IsDummy { get; }
    private bool _childrenLoaded;

    public FileSystemNodeViewModel(FileSystemNode node)
    {
        Node = node;
        _childrenLoaded = !node.IsDirectory;
        if (node.IsDirectory)
        {
            // Dummy-Eintrag damit der Expand-Pfeil bei Ordnern erscheint.
            Children.Add(new FileSystemNodeViewModel(node, dummy: true));
        }
    }

    private FileSystemNodeViewModel(FileSystemNode node, bool dummy)
    {
        Node = node;
        IsDummy = dummy;
        _childrenLoaded = true;
    }

    public ObservableCollection<FileSystemNodeViewModel> Children { get; } = new();

    public string Name => IsDummy ? "(lade...)" : Node.Name;
    public long Size => IsDummy ? 0 : Node.Size;
    public int FileCount => IsDummy ? 0 : Node.FileCount;
    public string FullPath => IsDummy ? "" : Node.FullPath;
    public bool IsDirectory => !IsDummy && Node.IsDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value) return;
        try { EnsureChildrenLoaded(); }
        catch (Exception ex) { Cleaner.App.App.LogException("EnsureChildrenLoaded", ex); }
    }

    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded) return;
        _childrenLoaded = true;
        Children.Clear();

        foreach (var c in Node.ChildrenSnapshot().OrderByDescending(c => c.Size))
            Children.Add(new FileSystemNodeViewModel(c));
    }

    /// <summary>
    /// Live-Update: refresht angezeigte Werte und synct evtl. neue Children. Rekursiv für
    /// alle EXPANDIERTEN Sub-VMs — Performance-Optimierung gegenüber Full-Tree-Walk.
    /// </summary>
    public void Refresh()
    {
        try
        {
            if (IsDummy) return;
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(FileCount));

            if (!_childrenLoaded || !IsExpanded) return;

            // Neue Children (während des Scans) einsortieren
            var existing = new HashSet<FileSystemNode>();
            foreach (var c in Children)
                if (!c.IsDummy) existing.Add(c.Node);

            var underlying = Node.ChildrenSnapshot();
            foreach (var n in underlying)
            {
                if (!existing.Contains(n))
                    Children.Add(new FileSystemNodeViewModel(n));
            }

            // Snapshot über ToArray, damit eine evtl. Mutation während Iteration nicht crasht
            var snapshot = Children.ToArray();
            foreach (var c in snapshot)
                if (c.IsExpanded) c.Refresh();
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("FileSystemNodeViewModel.Refresh", ex);
        }
    }
}
