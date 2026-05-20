using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

/// <summary>
/// Paralleler rekursiver Scanner für die Treemap-Ansicht. Macht den Baum sofort sichtbar
/// (Live-Update): Knoten werden während des Scans angelegt, Größen via Interlocked an
/// alle Ancestor-Knoten weitergegeben. Alle Children-Mutationen sind unter
/// FileSystemNode.SyncRoot serialisiert.
/// </summary>
public sealed class DiskScanner : IDiskScanner
{
    private long _bytesSeen;
    private int _filesSeen;

    public ScanSession StartScan(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = new FileSystemNode
        {
            Name = Path.GetFileName(rootPath.TrimEnd('\\')) is { Length: > 0 } n ? n : rootPath,
            FullPath = rootPath,
            IsDirectory = true,
        };

        _bytesSeen = 0;
        _filesSeen = 0;

        var completion = Task.Run(() => ScanRecursive(root, progress, ct), ct);
        return new ScanSession(root, completion);
    }

    private void ScanRecursive(FileSystemNode node, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        // Dateien in diesem Ordner
        try
        {
            foreach (var file in Directory.EnumerateFiles(node.FullPath))
            {
                if (ct.IsCancellationRequested) return;
                long size;
                DateTime lastWrite;
                try
                {
                    var fi = new FileInfo(file);
                    size = fi.Length;
                    lastWrite = fi.LastWriteTimeUtc;
                }
                catch { continue; }

                var fileNode = new FileSystemNode
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false,
                    Size = size,
                    FileCount = 1,
                    LastWriteUtc = lastWrite,
                    Parent = node,
                };

                lock (node.SyncRoot)
                {
                    node.Children.Add(fileNode);
                }
                BubbleUpSize(node, size, files: 1);

                var bytesTotal = Interlocked.Add(ref _bytesSeen, size);
                var filesTotal = Interlocked.Increment(ref _filesSeen);
                if ((filesTotal & 1023) == 0)
                    progress?.Report(new ScanProgress("disk-scan", file, bytesTotal, filesTotal));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (System.Security.SecurityException) { }

        // Unterordner parallel scannen
        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(node.FullPath);
        }
        catch { return; }

        try
        {
            Parallel.ForEach(
                subdirs,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
                },
                sub =>
                {
                    try
                    {
                        if (IsReparsePoint(sub)) return;

                        var subNode = new FileSystemNode
                        {
                            Name = Path.GetFileName(sub),
                            FullPath = sub,
                            IsDirectory = true,
                            Parent = node,
                        };

                        // Kind sofort eintragen → wird live im Treemap sichtbar
                        lock (node.SyncRoot)
                        {
                            node.Children.Add(subNode);
                        }

                        ScanRecursive(subNode, progress, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        // Einzelner Unter-Subtree-Fehler darf den Gesamtscan nicht killen.
                    }
                });
        }
        catch (OperationCanceledException) { /* propagiert von oben */ }
    }

    /// <summary>Erhöht Size/FileCount von start und allen Ancestors atomar.</summary>
    private static void BubbleUpSize(FileSystemNode start, long delta, int files)
    {
        var n = start;
        while (n is not null)
        {
            Interlocked.Add(ref n.Size, delta);
            Interlocked.Add(ref n.FileCount, files);
            n = n.Parent;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch { return false; }
    }
}
