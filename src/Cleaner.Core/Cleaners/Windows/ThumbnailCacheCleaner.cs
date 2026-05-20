using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class ThumbnailCacheCleaner : CleanupTargetBase
{
    public ThumbnailCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.thumbnail-cache";
    public override string Name => "Thumbnail-Cache";
    public override string Description =>
        "Vorschau-Bilder von Dateien (thumbcache_*.db). Windows baut den Cache bei Bedarf neu auf.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Safe;

    protected override string FilePattern => "thumbcache_*.db";

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "Windows", "Explorer");
    }
}
