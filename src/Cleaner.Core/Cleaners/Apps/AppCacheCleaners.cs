using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Apps;

/// <summary>Microsoft Teams Cache (klassisch + neues Teams).</summary>
public sealed class TeamsCacheCleaner : CleanupTargetBase
{
    public TeamsCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "app.teams-cache";
    public override string Name => "Microsoft Teams Cache";
    public override string Description =>
        "Cache von Teams (klassisch und neu). Chats/Einstellungen bleiben erhalten.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Other;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Teams Classic
        var classic = Path.Combine(roaming, "Microsoft", "Teams");
        foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache", "blob_storage", "tmp" })
            yield return Path.Combine(classic, sub);

        // Neues Teams (MSIX-Paket)
        var packages = Path.Combine(local, "Packages");
        var newTeams = Path.Combine(packages, "MSTeams_8wekyb3d8bbwe", "LocalCache");
        yield return newTeams;
    }
}

/// <summary>Slack Cache.</summary>
public sealed class SlackCacheCleaner : CleanupTargetBase
{
    public SlackCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "app.slack-cache";
    public override string Name => "Slack Cache";
    public override string Description =>
        "%APPDATA%\\Slack Cache-Ordner. Anmeldung und Einstellungen bleiben erhalten.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Other;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var slack = Path.Combine(roaming, "Slack");
        foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache", "Service Worker", "logs" })
            yield return Path.Combine(slack, sub);
    }
}

/// <summary>Discord Cache.</summary>
public sealed class DiscordCacheCleaner : CleanupTargetBase
{
    public DiscordCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "app.discord-cache";
    public override string Name => "Discord Cache";
    public override string Description =>
        "%APPDATA%\\discord Cache-Ordner. Anmeldung und Einstellungen bleiben erhalten.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Other;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var discord = Path.Combine(roaming, "discord");
        foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache" })
            yield return Path.Combine(discord, sub);
    }
}

/// <summary>Spotify Cache (Storage / Data).</summary>
public sealed class SpotifyCacheCleaner : CleanupTargetBase
{
    public SpotifyCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "app.spotify-cache";
    public override string Name => "Spotify Cache";
    public override string Description =>
        "%LocalAppData%\\Spotify Cache (zwischengespeicherte Songs). Wird neu aufgebaut.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Other;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var spotify = Path.Combine(local, "Spotify");
        yield return Path.Combine(spotify, "Storage");
        yield return Path.Combine(spotify, "Data");
        yield return Path.Combine(spotify, "Browser", "Cache");
    }
}
