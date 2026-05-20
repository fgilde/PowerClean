using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>
/// Findet bin/ und obj/ Ordner in Quellcode-Bäumen. Schaut nur dort, wo ein .sln oder .csproj
/// in der Nähe ist — räumt also nicht zufällig benannte bin-Ordner woanders.
/// </summary>
public sealed class DotNetBuildArtifactsCleaner : CleanupTargetBase
{
    public DotNetBuildArtifactsCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.dotnet-build-artifacts";
    public override string Name => ".NET Build-Artefakte (bin/obj)";
    public override string Description =>
        "Sucht bin/ und obj/ Ordner in deinen .NET-Projekten. Werden beim nächsten Build neu erstellt.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    /// <summary>Root-Ordner für die Suche. Default: gängige Dev-Pfade des Users.</summary>
    public IReadOnlyList<string> SearchRoots { get; set; } = DefaultSearchRoots();

    public static IReadOnlyList<string> DefaultSearchRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(profile, "source", "repos"),
            Path.Combine(profile, "Documents", "GitHub"),
            Path.Combine(profile, "dev"),
            Path.Combine(profile, "Projects"),
            Path.Combine(profile, "repos"),
            @"C:\dev",
            @"C:\src",
        };
        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public override bool IsAvailable() => SearchRoots.Any(Directory.Exists);

    public override Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long total = 0;
            int count = 0;
            var paths = new List<string>();
            int hits = 0;

            foreach (var root in SearchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in WalkProjects(root, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    foreach (var binObj in new[] { "bin", "obj" })
                    {
                        var path = Path.Combine(dir, binObj);
                        if (!Directory.Exists(path)) continue;

                        foreach (var file in SafeEnumerator.EnumerateFiles(path, "*", recursive: true))
                        {
                            paths.Add(file);
                            total += SafeEnumerator.TryGetSize(file);
                            count++;
                        }
                        hits++;
                        progress?.Report(new ScanProgress(Id, path, total, count));
                    }
                }
            }

            return new ScanResult { TargetId = Id, SizeBytes = total, FileCount = count, Paths = paths };
        }, ct);
    }

    private static IEnumerable<string> WalkProjects(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;

            var cur = stack.Pop();
            var name = Path.GetFileName(cur);

            // bin/obj/node_modules-Skip — wir suchen den PARENT
            if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isProject = false;
            try
            {
                isProject = Directory.EnumerateFiles(cur, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                         || Directory.EnumerateFiles(cur, "*.fsproj", SearchOption.TopDirectoryOnly).Any()
                         || Directory.EnumerateFiles(cur, "*.vbproj", SearchOption.TopDirectoryOnly).Any();
            }
            catch { continue; }

            if (isProject)
                yield return cur;

            foreach (var d in SafeEnumerator.EnumerateDirectories(cur))
                stack.Push(d);
        }
    }

    protected override IEnumerable<string> EnumerateCleanupRoots() => Array.Empty<string>();
}
