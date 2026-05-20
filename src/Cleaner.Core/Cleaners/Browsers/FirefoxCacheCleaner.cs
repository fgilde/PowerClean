using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Browsers;

public sealed class FirefoxCacheCleaner : CleanupTargetBase
{
    public FirefoxCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "browser.firefox-cache";
    public override string Name => "Firefox Cache";
    public override string Description => "cache2-Ordner aller Firefox-Profile.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Browsers;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var localProfiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mozilla", "Firefox", "Profiles");

        if (!Directory.Exists(localProfiles)) yield break;

        foreach (var profile in SafeEnumerator.EnumerateDirectories(localProfiles))
        {
            var cache2 = Path.Combine(profile, "cache2");
            if (Directory.Exists(cache2)) yield return cache2;

            var startupCache = Path.Combine(profile, "startupCache");
            if (Directory.Exists(startupCache)) yield return startupCache;
        }
    }
}
