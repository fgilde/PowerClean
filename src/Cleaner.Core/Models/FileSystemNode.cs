namespace Cleaner.Core.Models;

/// <summary>
/// Knoten im Ordner-/Datei-Baum für den Disk-Analyzer.
/// Während eines Live-Scans wächst der Baum im Hintergrund — alle Lese-/Schreibzugriffe
/// auf <see cref="Children"/> müssen über <see cref="SyncRoot"/> serialisiert werden.
/// </summary>
public sealed class FileSystemNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }

    /// <summary>Gesamtgröße inkl. aller Children (für Ordner) oder Dateigröße (für Dateien).</summary>
    public long Size;

    public int FileCount;
    public DateTime LastWriteUtc { get; set; }

    public FileSystemNode? Parent { get; set; }

    /// <summary>NICHT direkt lesen während ein Live-Scan läuft — stattdessen <see cref="ChildrenSnapshot"/>.</summary>
    public List<FileSystemNode> Children { get; } = new();

    /// <summary>Lock für Children-Zugriffe.</summary>
    public object SyncRoot { get; } = new();

    public FileSystemNode[] ChildrenSnapshot()
    {
        lock (SyncRoot)
        {
            return Children.ToArray();
        }
    }

    public override string ToString() => $"{(IsDirectory ? "[D]" : "[F]")} {FullPath} ({Size} B)";
}
