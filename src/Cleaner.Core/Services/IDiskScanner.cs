using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public interface IDiskScanner
{
    /// <summary>
    /// Startet einen Scan und liefert sofort die Wurzel zurück. Der Baum wird im Hintergrund
    /// gefüllt — Aufrufer kann <see cref="ScanSession.Completion"/> abwarten oder den Baum
    /// live anzeigen.
    /// </summary>
    ScanSession StartScan(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default);
}

public sealed record ScanSession(FileSystemNode Root, Task Completion);
