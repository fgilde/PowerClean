using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>
/// Findet node_modules-Ordner in Dev-Pfaden. Zeigt jeweils Größe und letztes Zugriffsdatum,
/// damit der User selbst entscheiden kann, ob es alte/stale-Projekte sind.
/// </summary>
public sealed class NodeModulesCleaner : CleanupTargetBase
{
    public NodeModulesCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.node-modules";
    public override string Name => "node_modules-Ordner";
    public override string Description =>
        "Sucht node_modules-Ordner in deinen Projekten. npm/pnpm/yarn install stellt sie wieder her. " +
        "Älter als 30 Tage werden bevorzugt vorgeschlagen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    public IReadOnlyList<string> SearchRoots { get; set; } = DotNetBuildArtifactsCleaner.DefaultSearchRoots();

    public override bool IsAvailable() => SearchRoots.Any(Directory.Exists);

    public override Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long total = 0;
            int count = 0;
            var paths = new List<string>();

            foreach (var root in SearchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var nm in WalkForNodeModules(root, ct))
                {
                    if (ct.IsCancellationRequested) break;
                    foreach (var file in SafeEnumerator.EnumerateFiles(nm, "*", recursive: true))
                    {
                        paths.Add(file);
                        total += SafeEnumerator.TryGetSize(file);
                        count++;
                    }
                    progress?.Report(new ScanProgress(Id, nm, total, count));
                }
            }

            return new ScanResult { TargetId = Id, SizeBytes = total, FileCount = count, Paths = paths };
        }, ct);
    }

    private static IEnumerable<string> WalkForNodeModules(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) yield break;
            var cur = stack.Pop();
            var name = Path.GetFileName(cur);

            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
            {
                yield return cur;
                continue; // nicht in node_modules tiefer suchen
            }

            foreach (var d in SafeEnumerator.EnumerateDirectories(cur))
                stack.Push(d);
        }
    }

    protected override IEnumerable<string> EnumerateCleanupRoots() => Array.Empty<string>();
}
