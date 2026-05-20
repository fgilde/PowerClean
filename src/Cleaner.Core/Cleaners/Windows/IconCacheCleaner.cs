using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class IconCacheCleaner : CleanupTargetBase
{
    public IconCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.icon-cache";
    public override string Name => "Icon-Cache";
    public override string Description => "iconcache_*.db Dateien. Wird vom Explorer neu erzeugt.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Safe;

    protected override string FilePattern => "iconcache_*.db";

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "Windows", "Explorer");
    }
}
