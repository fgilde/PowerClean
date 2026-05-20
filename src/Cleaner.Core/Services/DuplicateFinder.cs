using System.Collections.Concurrent;
using System.Security.Cryptography;
using Cleaner.Core.Models;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Services;

public sealed class DuplicateGroup
{
    public required long FileSize { get; init; }
    public required string Hash { get; init; }
    public required List<string> Paths { get; init; }
    public long WastedBytes => FileSize * Math.Max(0, Paths.Count - 1);
}

public interface IDuplicateFinder
{
    Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IReadOnlyList<string> searchRoots,
        long minFileSize,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class DuplicateFinder : IDuplicateFinder
{
    public async Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IReadOnlyList<string> searchRoots,
        long minFileSize,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Schritt 1: Alle Dateien sammeln, grouped by size — pro Such-Root parallel
        var bySize = new ConcurrentDictionary<long, ConcurrentBag<string>>();
        int totalFiles = 0;
        object seenGate = new();

        await Parallel.ForEachAsync(
            searchRoots,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Min(4, searchRoots.Count),
            },
            async (root, token) =>
            {
                if (!Directory.Exists(root)) return;
                await Task.Run(() =>
                {
                    foreach (var file in SafeEnumerator.EnumerateFiles(root, "*", recursive: true))
                    {
                        if (token.IsCancellationRequested) return;
                        long size = SafeEnumerator.TryGetSize(file);
                        if (size < minFileSize) continue;

                        bySize.GetOrAdd(size, _ => new ConcurrentBag<string>()).Add(file);
                        int n;
                        lock (seenGate) { n = ++totalFiles; }
                        if ((n & 511) == 0)
                            progress?.Report(new ScanProgress("dupe-scan", file, 0, n));
                    }
                }, token);
            });

        // Schritt 2: Bei Größen-Kollisionen Hash bilden
        var groups = new ConcurrentBag<DuplicateGroup>();
        var sizeBuckets = bySize.Where(kv => kv.Value.Count > 1).ToList();

        await Parallel.ForEachAsync(
            sizeBuckets,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
            async (bucket, token) =>
            {
                var byHash = new Dictionary<string, List<string>>();
                foreach (var path in bucket.Value)
                {
                    if (token.IsCancellationRequested) return;
                    var hash = await TryHashAsync(path, token);
                    if (hash is null) continue;
                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        list = new List<string>();
                        byHash[hash] = list;
                    }
                    list.Add(path);
                }

                foreach (var (hash, files) in byHash)
                {
                    if (files.Count > 1)
                        groups.Add(new DuplicateGroup
                        {
                            FileSize = bucket.Key,
                            Hash = hash,
                            Paths = files,
                        });
                }
            });

        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static async Task<string?> TryHashAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 16, useAsync: true);
            using var sha = SHA256.Create();
            var hashBytes = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hashBytes);
        }
        catch { return null; }
    }
}
