using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners;

/// <summary>
/// Basis-Implementierung für Cleaner, die einen oder mehrere Ordner aufräumen.
/// Subklassen liefern via <see cref="EnumerateCleanupRoots"/> die Wurzel-Pfade
/// und optional Filter (Datei-Pattern, Min-Alter, Ausschlüsse).
/// </summary>
public abstract class CleanupTargetBase : ICleanupTarget
{
    private readonly IFileSystemOperations _fileSystem;

    protected CleanupTargetBase(IFileSystemOperations fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string IconGlyph { get; }
    public abstract CleanupCategory Category { get; }
    public abstract SafetyLevel SafetyLevel { get; }
    public virtual bool RequiresAdmin => false;

    public virtual bool IsAvailable() => EnumerateCleanupRoots().Any(Directory.Exists);

    /// <summary>Liefert die Ordner, die rekursiv aufgeräumt werden. Subklassen überschreiben.</summary>
    protected abstract IEnumerable<string> EnumerateCleanupRoots();

    /// <summary>True, wenn die Wurzel-Ordner selbst NICHT gelöscht werden (typisch für Temp-Ordner).</summary>
    protected virtual bool PreserveRootDirectories => true;

    /// <summary>Optionales Pattern für Dateien (default: *).</summary>
    protected virtual string FilePattern => "*";

    /// <summary>Optional: Dateien jünger als das werden übersprungen. Default: kein Cutoff.</summary>
    protected virtual TimeSpan? MinimumAge => null;

    /// <summary>Optional: Pfade die niemals angefasst werden dürfen (Substring-Match, case-insensitive).</summary>
    protected virtual IEnumerable<string> ExcludePathSubstrings => Array.Empty<string>();

    public virtual Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long bytes = 0;
            int fileCount = 0;
            var paths = new List<string>();
            var errors = new List<string>();
            var cutoff = MinimumAge.HasValue ? DateTime.UtcNow - MinimumAge.Value : (DateTime?)null;
            var excludes = ExcludePathSubstrings.ToArray();

            foreach (var root in EnumerateCleanupRoots())
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var file in SafeEnumerator.EnumerateFiles(root, FilePattern, recursive: true))
                    {
                        if (ct.IsCancellationRequested) break;
                        if (IsExcluded(file, excludes)) continue;

                        if (cutoff.HasValue)
                        {
                            var ts = SafeEnumerator.TryGetLastWrite(file);
                            if (ts > cutoff.Value) continue;
                        }

                        long size = SafeEnumerator.TryGetSize(file);
                        bytes += size;
                        fileCount++;
                        paths.Add(file);

                        if ((fileCount & 63) == 0)
                            progress?.Report(new ScanProgress(Id, file, bytes, fileCount));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{root}: {ex.Message}");
                }
            }

            return new ScanResult
            {
                TargetId = Id,
                SizeBytes = bytes,
                FileCount = fileCount,
                Paths = paths,
                Errors = errors,
            };
        }, ct);
    }

    public virtual Task<CleanResult> CleanAsync(
        ScanResult scan,
        bool useRecycleBin,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long freed = 0;
            int deleted = 0;
            var errors = new List<string>();
            int total = scan.Paths.Count;

            foreach (var path in scan.Paths)
            {
                if (ct.IsCancellationRequested) break;

                long size = SafeEnumerator.TryGetSize(path);
                if (_fileSystem.DeleteFile(path, useRecycleBin))
                {
                    freed += size;
                    deleted++;
                }
                else
                {
                    errors.Add(path);
                }

                if ((deleted & 31) == 0)
                    progress?.Report(new CleanProgress(Id, path, freed, deleted, total));
            }

            // Leere Unterordner aufräumen (Wurzel selbst behalten wenn konfiguriert)
            foreach (var root in EnumerateCleanupRoots())
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    PruneEmptyDirectories(root, deleteSelf: !PreserveRootDirectories);
                }
                catch { /* ignore */ }
            }

            progress?.Report(new CleanProgress(Id, string.Empty, freed, deleted, total));

            return new CleanResult
            {
                TargetId = Id,
                FreedBytes = freed,
                FilesDeleted = deleted,
                Errors = errors,
            };
        }, ct);
    }

    private static bool IsExcluded(string path, string[] excludes)
    {
        if (excludes.Length == 0) return false;
        foreach (var ex in excludes)
            if (path.Contains(ex, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void PruneEmptyDirectories(string root, bool deleteSelf)
    {
        foreach (var sub in SafeEnumerator.EnumerateDirectories(root))
            PruneEmptyDirectories(sub, deleteSelf: true);

        try
        {
            if (!deleteSelf) return;
            if (!Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root, recursive: false);
        }
        catch { /* ignore */ }
    }
}
