using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Developer;

public sealed class VisualStudioCleaner : CleanupTargetBase
{
    public VisualStudioCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.visual-studio";
    public override string Name => "Visual Studio Caches";
    public override string Description =>
        "Component-Cache, ServiceHub-Logs, ComponentModelCache von Visual Studio. Werden neu erstellt.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Caution;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var vsRoot = Path.Combine(local, "Microsoft", "VisualStudio");

        if (Directory.Exists(vsRoot))
        {
            foreach (var ver in Utils.SafeEnumerator.EnumerateDirectories(vsRoot))
            {
                var name = Path.GetFileName(ver);
                if (!name.StartsWith("17.", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("16.", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("18.", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return Path.Combine(ver, "ComponentModelCache");
                yield return Path.Combine(ver, "ServiceHub", "Logs");
                yield return Path.Combine(ver, "VTC");
            }
        }

        // Setup-Caches
        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        var pkgCache = Path.Combine(programData, "Package Cache");
        // Achtung: Package Cache wird für VS-Reparaturen benutzt. Wir lassen es bewusst weg.
        // (User kann es manuell wegräumen.)
    }
}
