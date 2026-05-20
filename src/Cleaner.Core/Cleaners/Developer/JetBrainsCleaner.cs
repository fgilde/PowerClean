using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>
/// Räumt JetBrains-IDE-Caches und Logs auf — die "berüchtigten" Riesenordner in
/// %LOCALAPPDATA%\JetBrains. Settings (config) und Plugin-Daten werden NICHT angetastet.
/// </summary>
public sealed class JetBrainsCleaner : CleanupTargetBase
{
    public JetBrainsCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.jetbrains";
    public override string Name => "JetBrains IDE Caches & Logs";
    public override string Description =>
        "Caches, Logs, Indexes und Local History von Rider, IntelliJ, PyCharm, WebStorm, GoLand, PhpStorm, " +
        "RustRover, DataGrip, CLion. Werden beim nächsten IDE-Start neu erzeugt — die Re-Index-Phase kann " +
        "ein paar Minuten dauern. Konfiguration bleibt erhalten.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Caution;

    public override Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long size = 0;
            int count = 0;
            var paths = new List<string>();

            // Schlüsselverzeichnisse mit IDE-Daten
            foreach (var ideRoot in EnumerateIdeDataRoots())
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(ideRoot)) continue;

                // Nur "caches", "log", "tmp", "index", "local-history" wegräumen
                var sweepable = new[] { "caches", "log", "tmp", "index", "indexes", "local-history" };
                foreach (var sub in sweepable)
                {
                    var subPath = Path.Combine(ideRoot, sub);
                    if (!Directory.Exists(subPath)) continue;

                    foreach (var file in SafeEnumerator.EnumerateFiles(subPath, "*", recursive: true))
                    {
                        if (ct.IsCancellationRequested) break;
                        long s = SafeEnumerator.TryGetSize(file);
                        size += s;
                        count++;
                        paths.Add(file);
                        if ((count & 127) == 0)
                            progress?.Report(new ScanProgress(Id, file, size, count));
                    }
                }
            }

            return new ScanResult { TargetId = Id, SizeBytes = size, FileCount = count, Paths = paths };
        }, ct);
    }

    private static IEnumerable<string> EnumerateIdeDataRoots()
    {
        // Neue Toolbox-Layouts: %LOCALAPPDATA%\JetBrains\<Product><Version>
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var jetbrainsLocal = Path.Combine(local, "JetBrains");
        if (Directory.Exists(jetbrainsLocal))
        {
            foreach (var d in SafeEnumerator.EnumerateDirectories(jetbrainsLocal))
                yield return d;
        }

        // Ältere Layouts: %APPDATA%\JetBrains\<Product><Version>
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var jetbrainsRoaming = Path.Combine(roaming, "JetBrains");
        if (Directory.Exists(jetbrainsRoaming))
        {
            foreach (var d in SafeEnumerator.EnumerateDirectories(jetbrainsRoaming))
                yield return d;
        }

        // Legacy (vor 2020): %USERPROFILE%\.IntelliJIdea2019.3, .Rider2019.3, etc.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var d in SafeEnumerator.EnumerateDirectories(userProfile))
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith(".IntelliJ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".Rider", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".PyCharm", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".WebStorm", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".PhpStorm", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".GoLand", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".CLion", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".DataGrip", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(d, "system");
            }
        }
    }

    protected override IEnumerable<string> EnumerateCleanupRoots() => Array.Empty<string>();
}
