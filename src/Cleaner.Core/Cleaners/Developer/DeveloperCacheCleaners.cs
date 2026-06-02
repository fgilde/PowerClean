using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>pip Download-Cache (%LocalAppData%\pip\Cache, %APPDATA%\pip).</summary>
public sealed class PipCacheCleaner : CleanupTargetBase
{
    public PipCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.pip-cache";
    public override string Name => "Python pip Cache";
    public override string Description =>
        "Download-Cache von pip. Wird beim nächsten Installieren neu aufgebaut.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Safe;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(local, "pip", "Cache");
        yield return Path.Combine(roaming, "pip", "Cache");
    }
}

/// <summary>Maven lokales Repository (~/.m2/repository).</summary>
public sealed class MavenCacheCleaner : CleanupTargetBase
{
    public MavenCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.maven-repo";
    public override string Name => "Maven Repository (.m2)";
    public override string Description =>
        "~/.m2/repository — heruntergeladene Maven-Abhängigkeiten. Werden bei Bedarf neu geladen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, ".m2", "repository");
    }
}

/// <summary>Gradle Caches (~/.gradle/caches).</summary>
public sealed class GradleCacheCleaner : CleanupTargetBase
{
    public GradleCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.gradle-cache";
    public override string Name => "Gradle Cache";
    public override string Description =>
        "~/.gradle/caches — Gradle Build- und Dependency-Cache. Wird neu aufgebaut.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, ".gradle", "caches");
    }
}

/// <summary>Go Build- und Modul-Cache (%LocalAppData%\go-build, GOPATH/pkg/mod).</summary>
public sealed class GoCacheCleaner : CleanupTargetBase
{
    public GoCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.go-cache";
    public override string Name => "Go Build/Module Cache";
    public override string Description =>
        "Go Build-Cache und heruntergeladene Module. Werden bei Bedarf neu geladen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(local, "go-build");

        // GOPATH (Standard: ~/go) — pkg/mod ist read-only, wird beim Löschen entsperrt.
        var gopath = Environment.GetEnvironmentVariable("GOPATH");
        var root = string.IsNullOrWhiteSpace(gopath) ? Path.Combine(profile, "go") : gopath;
        yield return Path.Combine(root, "pkg", "mod", "cache", "download");
    }
}

/// <summary>Rust/Cargo Registry-Cache (~/.cargo/registry/cache).</summary>
public sealed class CargoCacheCleaner : CleanupTargetBase
{
    public CargoCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.cargo-cache";
    public override string Name => "Rust Cargo Cache";
    public override string Description =>
        "~/.cargo/registry — heruntergeladene Crate-Archive. Werden bei Bedarf neu geladen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, ".cargo", "registry", "cache");
        yield return Path.Combine(profile, ".cargo", "registry", "src");
    }
}
