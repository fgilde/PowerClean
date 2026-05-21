using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cleaner.Core.Models;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Services;

public sealed class LogFileEntry
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public required string Root { get; init; }   // Label des Such-Root, z.B. "Temp", "Roaming", "Windows-Logs"
    public required string Pattern { get; init; } // welches Pattern hat gematcht
}

public sealed class LogFinderOptions
{
    public required IReadOnlyList<string> Patterns { get; init; }
    public required TimeSpan MinAge { get; init; }
    public IReadOnlyList<string> ExtraRoots { get; init; } = Array.Empty<string>();
}

public interface ILogFinder
{
    Task<IReadOnlyList<LogFileEntry>> FindAsync(
        LogFinderOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class LogFinder : ILogFinder
{
    /// <summary>
    /// Default-Such-Roots laut Spezifikation. Verwendet strikt SpecialFolder/Path.GetTempPath()
    /// statt hardgecodeter Pfade, damit das auch auf nicht-standard Windows-Installationen funktioniert.
    /// Roots die nicht existieren werden später beim Scan einfach übersprungen.
    /// </summary>
    private static IReadOnlyList<(string Path, string Label)> GetDefaultRoots()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var tempUser = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tempLocalApp = string.IsNullOrEmpty(localApp)
            ? null
            : Path.Combine(localApp, "Temp");

        var list = new List<(string, string)>
        {
            (tempUser, "Temp"),
        };

        // Nur dazu wenn anders als Path.GetTempPath (sehr oft sind sie identisch)
        if (!string.IsNullOrEmpty(tempLocalApp) &&
            !string.Equals(NormalizePath(tempLocalApp), NormalizePath(tempUser), StringComparison.OrdinalIgnoreCase))
        {
            list.Add((tempLocalApp, "LocalAppData-Temp"));
        }

        if (!string.IsNullOrEmpty(roaming)) list.Add((roaming, "Roaming"));
        if (!string.IsNullOrEmpty(localApp)) list.Add((localApp, "Local"));
        if (!string.IsNullOrEmpty(programData)) list.Add((programData, "ProgramData"));

        if (!string.IsNullOrEmpty(windows))
        {
            list.Add((Path.Combine(windows, "Logs"), "Windows-Logs"));
            list.Add((Path.Combine(windows, "Panther"), "Windows-Panther"));
            list.Add((Path.Combine(windows, "System32", "winevt", "Logs"), "EventLogs"));
        }

        return list;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }

    public async Task<IReadOnlyList<LogFileEntry>> FindAsync(
        LogFinderOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (options.Patterns.Count == 0) return Array.Empty<LogFileEntry>();

        // Pattern → Regex einmalig kompilieren. Glob-Subset: '*' und '?' werden unterstützt.
        var compiled = options.Patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => (Pattern: p.Trim(), Regex: GlobToRegex(p.Trim())))
            .ToList();

        if (compiled.Count == 0) return Array.Empty<LogFileEntry>();

        var defaults = GetDefaultRoots();
        var roots = new List<(string Path, string Label)>(defaults);
        foreach (var extra in options.ExtraRoots)
        {
            if (string.IsNullOrWhiteSpace(extra)) continue;
            roots.Add((extra, Path.GetFileName(extra.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                              is var label && string.IsNullOrEmpty(label) ? extra : label));
        }

        var bag = new ConcurrentBag<LogFileEntry>();
        var cutoffUtc = DateTime.UtcNow - options.MinAge;
        int seen = 0;
        object seenGate = new();

        await Parallel.ForEachAsync(
            roots,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, roots.Count)),
            },
            async (root, token) =>
            {
                if (!Directory.Exists(root.Path)) return;
                await Task.Run(() =>
                {
                    foreach (var file in SafeEnumerator.EnumerateFiles(root.Path, "*", recursive: true))
                    {
                        if (token.IsCancellationRequested) break;

                        int s;
                        lock (seenGate) { s = ++seen; }

                        var fileName = Path.GetFileName(file);
                        var matched = MatchPattern(fileName, compiled);
                        if (matched is null)
                        {
                            if ((s & 4095) == 0)
                                progress?.Report(new ScanProgress("log-finder", file, 0, s));
                            continue;
                        }

                        var lastWrite = SafeEnumerator.TryGetLastWrite(file);
                        if (lastWrite == DateTime.MinValue) continue;
                        if (lastWrite > cutoffUtc) continue;

                        var size = SafeEnumerator.TryGetSize(file);

                        bag.Add(new LogFileEntry
                        {
                            Path = file,
                            Size = size,
                            LastWriteUtc = lastWrite,
                            Root = root.Label,
                            Pattern = matched,
                        });

                        if ((s & 1023) == 0)
                            progress?.Report(new ScanProgress("log-finder", file, size, s));
                    }
                }, token);
            }).ConfigureAwait(false);

        // Lock-Test NUR auf dem finalen Result-Set machen — ein File.Open pro Datei
        // ist teuer, das wollen wir nicht für jeden gescannten Pfad zahlen.
        var candidates = bag.ToArray();
        var freeBag = new ConcurrentBag<LogFileEntry>();

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, candidates.Length)),
            },
            (entry, token) =>
            {
                if (token.IsCancellationRequested) return ValueTask.CompletedTask;
                if (!IsLocked(entry.Path)) freeBag.Add(entry);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return freeBag
            .OrderByDescending(r => r.Size)
            .ToList();
    }

    private static string? MatchPattern(string fileName, IReadOnlyList<(string Pattern, Regex Regex)> compiled)
    {
        foreach (var (pattern, regex) in compiled)
        {
            if (regex.IsMatch(fileName)) return pattern;
        }
        return null;
    }

    /// <summary>
    /// Konvertiert ein simples Glob-Pattern ('*', '?') in einen Regex der den ganzen
    /// Dateinamen matcht (anchored, case-insensitive). Alle anderen Zeichen werden escaped.
    /// </summary>
    private static Regex GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        foreach (var ch in glob)
        {
            sb.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>
    /// Versucht die Datei exklusiv (FileShare.None) zu öffnen. Wenn das gelingt, ist die Datei
    /// nicht von einem anderen Prozess in Benutzung und kann sicher gelöscht werden.
    /// </summary>
    private static bool IsLocked(string path)
    {
        try
        {
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }
}
