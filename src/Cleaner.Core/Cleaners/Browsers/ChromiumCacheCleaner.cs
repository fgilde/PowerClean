using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Browsers;

/// <summary>
/// Generischer Cleaner für Chromium-basierte Browser. Räumt nur Cache-Ordner,
/// niemals Bookmarks, History oder Cookies.
/// </summary>
public abstract class ChromiumCacheCleaner : CleanupTargetBase
{
    protected ChromiumCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override CleanupCategory Category => CleanupCategory.Browsers;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected abstract string UserDataRoot { get; }

    // Wir räumen die "Cache"-Subordner aller Profile, nicht das Profil selbst
    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        if (!Directory.Exists(UserDataRoot)) yield break;

        foreach (var profile in Utils.SafeEnumerator.EnumerateDirectories(UserDataRoot))
        {
            var name = Path.GetFileName(profile);
            // Profil-Ordner heißen "Default", "Profile 1", "Profile 2", ...
            if (!name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var cache in new[] {
                Path.Combine(profile, "Cache"),
                Path.Combine(profile, "Code Cache"),
                Path.Combine(profile, "GPUCache"),
                Path.Combine(profile, "Service Worker", "CacheStorage"),
                Path.Combine(profile, "Service Worker", "ScriptCache"),
            })
            {
                if (Directory.Exists(cache)) yield return cache;
            }
        }
    }
}

public sealed class ChromeCacheCleaner : ChromiumCacheCleaner
{
    public ChromeCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "browser.chrome-cache";
    public override string Name => "Google Chrome Cache";
    public override string Description => "Cache, GPUCache, Service-Worker-Cache aller Chrome-Profile.";
    public override string IconGlyph => "";

    protected override string UserDataRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Google", "Chrome", "User Data");
        }
    }
}

public sealed class EdgeCacheCleaner : ChromiumCacheCleaner
{
    public EdgeCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "browser.edge-cache";
    public override string Name => "Microsoft Edge Cache";
    public override string Description => "Cache, GPUCache, Service-Worker-Cache aller Edge-Profile.";
    public override string IconGlyph => "";

    protected override string UserDataRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Microsoft", "Edge", "User Data");
        }
    }
}

public sealed class BraveCacheCleaner : ChromiumCacheCleaner
{
    public BraveCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "browser.brave-cache";
    public override string Name => "Brave Cache";
    public override string Description => "Cache, GPUCache, Service-Worker-Cache aller Brave-Profile.";
    public override string IconGlyph => "";

    protected override string UserDataRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data");
        }
    }
}
