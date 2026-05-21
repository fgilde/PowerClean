using System.Collections.Concurrent;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Services;

public sealed class OrphanUserDataEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Root { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModifiedUtc { get; init; }
    public required string Reason { get; init; }
}

public interface IOrphanUserDataScanner
{
    Task<IReadOnlyList<OrphanUserDataEntry>> ScanAsync(CancellationToken ct = default);
}

public sealed class OrphanUserDataScanner : IOrphanUserDataScanner
{
    private readonly IInstalledProgramsScanner _programs;

    public OrphanUserDataScanner(IInstalledProgramsScanner programs)
    {
        _programs = programs;
    }

    /// <summary>Frische Ordner unter dieser Grenze gelten potenziell noch zu einer laufenden Installation.</summary>
    private static readonly TimeSpan MinAge = TimeSpan.FromDays(14);

    /// <summary>
    /// Hardgecodete Vendor- / Top-Level-Whitelist. Diese Top-Level-Namen werden NIE als orphan markiert,
    /// auch wenn sie nicht in den installierten Programmen auftauchen — sie gehören zu Windows oder
    /// zu sehr verbreiteten Hersteller-Wurzeln.
    /// </summary>
    private static readonly string[] KnownVendors =
    {
        // System / OS
        "Microsoft", "Windows", "WindowsApps", "Packages", "ConnectedDevicesPlatform",
        "Comms", "CrashDumps", "D3DSCache", "ElevatedDiagnostics", "MicrosoftEdge",
        "Microsoft Corporation", "Microsoft_Corporation", "MicrosoftEdgeBackups",
        "Publishers", "VirtualStore", "Temp", "TempState", "Local", "Roaming", "LocalLow",
        "History", "IconCache", "INetCache", "INetCookies", "Diagnostics", "Application Data",
        "PeerDistRepub", "PlaceholderTileLogoFolder", "DBG", "Downloaded Installations",

        // Common vendors with stable folder names
        "Adobe", "Google", "Mozilla", "Apple", "Apple Computer", "Apple Inc",
        "JetBrains", "Slack", "discord", "Discord", "Spotify", "OBSStudio", "obs-studio",
        "GitHub", "GitHub Desktop", "GitHubDesktop", "NVIDIA", "NVIDIA Corporation",
        "Intel", "AMD", "Logitech", "Razer", "Steam", "Valve", "EpicGamesLauncher",
        "Zoom", "Skype", "Teams", "OneDrive", "Dropbox", "Box", "Notion",
        "1Password", "Bitwarden", "LastPass", "Postman", "Insomnia",
        "Brave-Browser", "BraveSoftware", "Vivaldi", "Opera", "Opera Software",
        "Code", "Code - Insiders", "VSCodium", "Cursor", "Trae",

        // Dev/CLI toolchains
        "Programs", "npm-cache", "npm", "Yarn", "yarn", "pnpm", "pnpm-cache",
        "Composer", "Cargo", ".cargo", ".rustup", "rustup", "go-build", "GoLand",
        "Python", "pip", "pipx", "uv", "poetry", "PyCharm",
        "NuGet", "DotNet", "dotnet", ".dotnet", "powershell", "PowerShell",
    };

    public async Task<IReadOnlyList<OrphanUserDataEntry>> ScanAsync(CancellationToken ct = default)
    {
        var installed = await _programs.ScanAsync(ct).ConfigureAwait(false);

        // Pre-compute lowercase needles aus DisplayName und Publisher
        var needles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in installed)
        {
            AddNeedles(needles, p.DisplayName);
            AddNeedles(needles, p.Publisher);
        }

        var whitelist = new HashSet<string>(KnownVendors, StringComparer.OrdinalIgnoreCase);

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localLow = Path.GetFullPath(Path.Combine(local, "..", "LocalLow"));

        var roots = new (string Path, string Label)[]
        {
            (roaming, "Roaming"),
            (local, "Local"),
            (localLow, "LocalLow"),
        };

        var bag = new ConcurrentBag<OrphanUserDataEntry>();
        var now = DateTime.UtcNow;

        await Parallel.ForEachAsync(
            roots,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 3 },
            async (root, token) =>
            {
                if (!Directory.Exists(root.Path)) return;
                await Task.Run(() => ScanRoot(root.Path, root.Label, whitelist, needles, now, bag, token), token);
            }).ConfigureAwait(false);

        return bag
            .OrderByDescending(e => e.SizeBytes)
            .ToList();
    }

    private static void ScanRoot(
        string rootPath, string label,
        HashSet<string> whitelist, HashSet<string> needles,
        DateTime now,
        ConcurrentBag<OrphanUserDataEntry> bag,
        CancellationToken ct)
    {
        foreach (var dir in SafeEnumerator.EnumerateDirectories(rootPath))
        {
            if (ct.IsCancellationRequested) return;

            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) continue;

            if (whitelist.Contains(name)) continue;

            DateTime lastWrite;
            try { lastWrite = Directory.GetLastWriteTimeUtc(dir); }
            catch { continue; }

            if (now - lastWrite < MinAge) continue;

            if (IsKnownProgramFolder(name, needles, out var matchedTerm))
            {
                _ = matchedTerm;
                continue;
            }

            // Hat der Ordner überhaupt Inhalt?
            long size = 0;
            bool hasAny = false;
            try
            {
                foreach (var f in SafeEnumerator.EnumerateFiles(dir, "*", recursive: true))
                {
                    if (ct.IsCancellationRequested) return;
                    hasAny = true;
                    size += SafeEnumerator.TryGetSize(f);
                }
            }
            catch { /* ignore — bestcase-Größe ist genug */ }

            if (!hasAny) continue;

            var reason = $"Kein installiertes Programm passt zu '{name}'.";
            bag.Add(new OrphanUserDataEntry
            {
                Name = name,
                FullPath = dir,
                Root = label,
                SizeBytes = size,
                LastModifiedUtc = lastWrite,
                Reason = reason,
            });
        }
    }

    private static bool IsKnownProgramFolder(string folderName, HashSet<string> needles, out string? matched)
    {
        matched = null;
        if (needles.Count == 0) return false;

        // Direktes Match
        if (needles.Contains(folderName)) { matched = folderName; return true; }

        // Partial Match: Ordnername Substring eines Needles ODER Needle Substring vom Ordnernamen.
        // Beide Richtungen weil DisplayNames oft länger sind als der Ordner ("Adobe Photoshop CC 2024"
        // vs Ordner "Photoshop") aber manchmal auch kürzer ("Photoshop" vs Ordner "Adobe Photoshop").
        foreach (var n in needles)
        {
            if (n.Length < 3) continue; // zu kurze Tokens (z.B. "Co") matchen alles
            if (folderName.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                n.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                matched = n;
                return true;
            }
        }

        // Sub-token aus Ordner trennen: "Some.Vendor_Tool" → ["Some", "Vendor", "Tool"]
        foreach (var token in folderName.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 3) continue;
            if (needles.Contains(token)) { matched = token; return true; }
        }

        return false;
    }

    private static void AddNeedles(HashSet<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        set.Add(trimmed);

        // Häufige Suffixe / Filler entfernen, damit "Acme Software, Inc." auch als "Acme" matcht
        foreach (var token in trimmed.Split(new[] { ' ', ',', '.', '_', '-', '(', ')' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 3 && !IsBoilerplate(token))
                set.Add(token);
        }
    }

    private static bool IsBoilerplate(string token) => token.ToLowerInvariant() switch
    {
        "inc" or "ltd" or "llc" or "gmbh" or "corp" or "corporation"
            or "co" or "company" or "software" or "the" or "and" or "for"
            or "x86" or "x64" or "version" => true,
        _ => false,
    };
}
