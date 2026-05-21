using System.Collections.Concurrent;
using System.Security.Cryptography;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Services;

public enum CompareStatus
{
    Equal,
    Different,
    LeftOnly,
    RightOnly,
}

public sealed class CompareEntry
{
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public CompareStatus Status { get; init; }
    public long LeftSize { get; init; }
    public long RightSize { get; init; }
    public DateTime? LeftLastWriteUtc { get; init; }
    public DateTime? RightLastWriteUtc { get; init; }
    public string? LeftFullPath { get; init; }
    public string? RightFullPath { get; init; }
}

public readonly record struct CompareProgress(string CurrentPath, int FilesProcessed);

public interface IFolderCompareService
{
    Task<IReadOnlyList<CompareEntry>> CompareAsync(
        string leftRoot,
        string rightRoot,
        IProgress<CompareProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class FolderCompareService : IFolderCompareService
{
    private sealed record FileMeta(long Size, DateTime LastWriteUtc, string FullPath);

    public async Task<IReadOnlyList<CompareEntry>> CompareAsync(
        string leftRoot,
        string rightRoot,
        IProgress<CompareProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leftRoot) || !Directory.Exists(leftRoot))
            throw new DirectoryNotFoundException($"Linker Ordner nicht gefunden: {leftRoot}");
        if (string.IsNullOrWhiteSpace(rightRoot) || !Directory.Exists(rightRoot))
            throw new DirectoryNotFoundException($"Rechter Ordner nicht gefunden: {rightRoot}");

        var leftFull = Path.GetFullPath(leftRoot).TrimEnd('\\', '/');
        var rightFull = Path.GetFullPath(rightRoot).TrimEnd('\\', '/');

        // Schritt 1: Beide Bäume parallel enumerieren
        Dictionary<string, FileMeta> left = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FileMeta> right = new(StringComparer.OrdinalIgnoreCase);

        var leftTask = Task.Run(() => EnumerateRoot(leftFull, ct), ct);
        var rightTask = Task.Run(() => EnumerateRoot(rightFull, ct), ct);
        await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);
        left = leftTask.Result;
        right = rightTask.Result;

        ct.ThrowIfCancellationRequested();

        // Schritt 2: Schlüssel-Vereinigung
        var allKeys = new HashSet<string>(left.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var k in right.Keys) allKeys.Add(k);

        // Schritt 3: Pro Eintrag Status bestimmen — Hash nur wenn Größen gleich
        var results = new ConcurrentBag<CompareEntry>();
        int processed = 0;

        // Trivial-Fälle (LeftOnly, RightOnly, unterschiedliche Größe) zuerst,
        // gleichgroße Files für parallelen Hash-Vergleich sammeln.
        var hashCandidates = new List<(string rel, FileMeta lm, FileMeta rm)>();

        foreach (var rel in allKeys)
        {
            ct.ThrowIfCancellationRequested();
            bool hasL = left.TryGetValue(rel, out var lm);
            bool hasR = right.TryGetValue(rel, out var rm);

            if (hasL && !hasR)
            {
                results.Add(new CompareEntry
                {
                    RelativePath = rel,
                    IsDirectory = false,
                    Status = CompareStatus.LeftOnly,
                    LeftSize = lm!.Size,
                    LeftLastWriteUtc = lm.LastWriteUtc,
                    LeftFullPath = lm.FullPath,
                });
            }
            else if (!hasL && hasR)
            {
                results.Add(new CompareEntry
                {
                    RelativePath = rel,
                    IsDirectory = false,
                    Status = CompareStatus.RightOnly,
                    RightSize = rm!.Size,
                    RightLastWriteUtc = rm.LastWriteUtc,
                    RightFullPath = rm.FullPath,
                });
            }
            else if (hasL && hasR)
            {
                if (lm!.Size != rm!.Size)
                {
                    results.Add(new CompareEntry
                    {
                        RelativePath = rel,
                        IsDirectory = false,
                        Status = CompareStatus.Different,
                        LeftSize = lm.Size,
                        RightSize = rm.Size,
                        LeftLastWriteUtc = lm.LastWriteUtc,
                        RightLastWriteUtc = rm.LastWriteUtc,
                        LeftFullPath = lm.FullPath,
                        RightFullPath = rm.FullPath,
                    });
                }
                else
                {
                    hashCandidates.Add((rel, lm, rm));
                }
            }
        }

        // Schritt 4: Hash-Vergleich bei gleicher Größe — Parallel, aber begrenzt für SSDs.
        var po = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount),
        };

        await Parallel.ForEachAsync(hashCandidates, po, async (item, token) =>
        {
            var (rel, lm, rm) = item;
            string? leftHash = await TryHashAsync(lm.FullPath, token).ConfigureAwait(false);
            string? rightHash = await TryHashAsync(rm.FullPath, token).ConfigureAwait(false);

            bool equal = leftHash is not null
                         && rightHash is not null
                         && string.Equals(leftHash, rightHash, StringComparison.Ordinal);

            results.Add(new CompareEntry
            {
                RelativePath = rel,
                IsDirectory = false,
                Status = equal ? CompareStatus.Equal : CompareStatus.Different,
                LeftSize = lm.Size,
                RightSize = rm.Size,
                LeftLastWriteUtc = lm.LastWriteUtc,
                RightLastWriteUtc = rm.LastWriteUtc,
                LeftFullPath = lm.FullPath,
                RightFullPath = rm.FullPath,
            });

            int n = Interlocked.Increment(ref processed);
            if ((n & 31) == 0)
                progress?.Report(new CompareProgress(rel, n));
        }).ConfigureAwait(false);

        progress?.Report(new CompareProgress(string.Empty, processed));

        return results
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, FileMeta> EnumerateRoot(string root, CancellationToken ct)
    {
        var dict = new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase);
        int rootLen = root.Length + 1; // +1 für trailing separator

        foreach (var file in SafeEnumerator.EnumerateFiles(root, "*", recursive: true))
        {
            if (ct.IsCancellationRequested) break;

            string rel;
            if (file.Length > rootLen && file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && (file[root.Length] == '\\' || file[root.Length] == '/'))
            {
                rel = file[rootLen..];
            }
            else
            {
                // Fallback wenn aus irgendeinem Grund das Präfix nicht stimmt
                rel = Path.GetRelativePath(root, file);
            }
            rel = rel.Replace('/', '\\');

            long size = SafeEnumerator.TryGetSize(file);
            DateTime mtime = SafeEnumerator.TryGetLastWrite(file);

            dict[rel] = new FileMeta(size, mtime, file);
        }

        return dict;
    }

    private static async Task<string?> TryHashAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 16, useAsync: true);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
        catch { return null; }
    }
}
