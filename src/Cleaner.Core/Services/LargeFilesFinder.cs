using System.Collections.Concurrent;
using Cleaner.Core.Models;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Services;

public sealed class LargeFileEntry
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required DateTime LastWriteUtc { get; init; }
}

public interface ILargeFilesFinder
{
    Task<IReadOnlyList<LargeFileEntry>> FindAsync(
        IReadOnlyList<string> searchRoots,
        long minSize,
        int maxResults,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class LargeFilesFinder : ILargeFilesFinder
{
    public async Task<IReadOnlyList<LargeFileEntry>> FindAsync(
        IReadOnlyList<string> searchRoots,
        long minSize,
        int maxResults,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<LargeFileEntry>();
        int seen = 0;
        object seenGate = new();

        // Parallel pro Search-Root — mehrere Festplatten / Pfade gleichzeitig scannen
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
                        if (token.IsCancellationRequested) break;

                        long size = SafeEnumerator.TryGetSize(file);
                        int s;
                        lock (seenGate) { s = ++seen; }

                        if (size >= minSize)
                        {
                            bag.Add(new LargeFileEntry
                            {
                                Path = file,
                                Size = size,
                                LastWriteUtc = SafeEnumerator.TryGetLastWrite(file),
                            });
                        }

                        if ((s & 1023) == 0)
                            progress?.Report(new ScanProgress("large-files", file, size, s));
                    }
                }, token);
            });

        return bag
            .OrderByDescending(r => r.Size)
            .Take(maxResults)
            .ToList();
    }
}
