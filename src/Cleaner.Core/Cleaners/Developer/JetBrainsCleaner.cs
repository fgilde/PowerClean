using System.Text.Json;
using System.Text.RegularExpressions;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>
/// Räumt JetBrains-IDE-Caches und Logs auf — die "berüchtigten" Riesenordner in
/// %LOCALAPPDATA%\JetBrains. Erkennt zusätzlich VERALTETE Installationen (z.B. Rider2023.3,
/// nachdem Rider2024.x existiert / installiert wurde) und schlägt deren komplette
/// Produktordner zum Aufräumen vor. Settings (config) der aktuell installierten Versionen
/// werden NICHT angetastet.
/// </summary>
public sealed class JetBrainsCleaner : CleanupTargetBase
{
    public JetBrainsCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "dev.jetbrains";
    public override string Name => "JetBrains IDE Caches & Logs";
    public override string Description =>
        "Caches, Logs, Indexes und Local History von Rider, IntelliJ, PyCharm, WebStorm, GoLand, PhpStorm, " +
        "RustRover, DataGrip, CLion. Werden beim nächsten IDE-Start neu erzeugt — die Re-Index-Phase kann " +
        "ein paar Minuten dauern. Konfiguration der aktuellen Versionen bleibt erhalten. " +
        "Erkennt zusätzlich veraltete Installationen (z.B. nach IDE-Upgrade verbliebene Ordner älterer Versionen) " +
        "und entfernt diese komplett — wenn die JetBrains Toolbox installiert ist, wird deren App-Liste als " +
        "Quelle der aktuell installierten Versionen herangezogen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Developer;
    public override SafetyLevel SafetyLevel => SafetyLevel.Caution;

    // Bekannte JetBrains-Produktpräfixe — alles was so anfängt + "<Version>" wird gematcht.
    private static readonly string[] KnownProducts =
    {
        "IntelliJIdea", "IdeaIC", "AndroidStudio", "Rider", "PyCharm", "PyCharmCE",
        "WebStorm", "GoLand", "PhpStorm", "RustRover", "DataGrip", "CLion",
        "AppCode", "DataSpell", "RubyMine", "Aqua", "Writerside", "MPS", "Fleet",
    };

    // Matches "<ProductName><Version>" where version starts with a digit, e.g. "Rider2024.1", "IntelliJIdea2022.1.3".
    private static readonly Regex ProductVersionRegex =
        new(@"^(?<product>[A-Za-z]+?)(?<version>\d[\w.\-]*)$", RegexOptions.Compiled);

    public override Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long size = 0;
            int count = 0;
            var paths = new List<string>();

            // 1. Sammle alle <Product><Version>-Ordner aus beiden Daten-Roots.
            var productDirs = EnumerateProductDirs().ToList();

            // 2. Ermittle "aktive" Versionen via Toolbox (falls vorhanden) oder per Höchste-Version-Heuristik.
            //    obsoleteRoots = Pfade von Produktordnern, die als veraltet gelten -> KOMPLETT löschbar.
            //    activeRoots   = Pfade aktueller Produktordner -> nur Caches/Logs/etc. wegräumen.
            var (activeRoots, obsoleteRoots) = ClassifyInstallations(productDirs);

            // 3a. Aktive Installationen: nur die bekannten "Sweep"-Unterordner wegräumen (bisheriges Verhalten).
            var sweepable = new[] { "caches", "log", "tmp", "index", "indexes", "local-history" };
            foreach (var ideRoot in activeRoots)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(ideRoot)) continue;

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

            // 3b. Veraltete Installationen: KOMPLETTER Produktordner ist Müll (config, plugins, system, caches).
            //     Diese Pfade sind sicher löschbar, sofern die IDE wirklich deinstalliert wurde — was bei
            //     verbleibenden Ordnern älterer Versionen praktisch immer der Fall ist.
            foreach (var obsoleteRoot in obsoleteRoots)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(obsoleteRoot)) continue;

                foreach (var file in SafeEnumerator.EnumerateFiles(obsoleteRoot, "*", recursive: true))
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

            // 3c. Legacy-Layouts (vor 2020) — wie bisher: %USERPROFILE%\.IntelliJIdea2019.3\system etc.
            foreach (var legacySystem in EnumerateLegacySystemDirs())
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(legacySystem)) continue;

                foreach (var file in SafeEnumerator.EnumerateFiles(legacySystem, "*", recursive: true))
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

            return new ScanResult { TargetId = Id, SizeBytes = size, FileCount = count, Paths = paths };
        }, ct);
    }

    /// <summary>
    /// Eintrag eines erkannten Produktordners: physischer Pfad + geparster Name (Product, Version).
    /// </summary>
    private readonly record struct ProductDir(string Path, string Product, string Version);

    /// <summary>
    /// Enumeriert alle JetBrains-Produktordner unterhalb von %LOCALAPPDATA%\JetBrains und %APPDATA%\JetBrains,
    /// deren Namen dem Schema "<Product><Version>" entsprechen (Toolbox/standalone since 2020).
    /// Toolbox- bzw. Helper-Ordner (z.B. "Toolbox", "consentOptions") werden übersprungen.
    /// </summary>
    private static IEnumerable<ProductDir> EnumerateProductDirs()
    {
        foreach (var root in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetBrains"),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeEnumerator.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                var m = ProductVersionRegex.Match(name);
                if (!m.Success) continue;
                var product = m.Groups["product"].Value;
                // Nur bekannte Produkte zählen — verhindert False Positives bei zukünftigen Helper-Ordnern.
                if (!KnownProducts.Any(p => string.Equals(p, product, StringComparison.OrdinalIgnoreCase)))
                    continue;
                yield return new ProductDir(dir, product, m.Groups["version"].Value);
            }
        }
    }

    /// <summary>
    /// Teilt die gefundenen Produktordner in (aktive, veraltete) auf.
    /// Strategie:
    ///   1. Wenn die JetBrains Toolbox installiert ist und ihre App-Liste lesbar ist:
    ///      Alle Produktordner, deren (Product, Version)-Kombination NICHT in der Toolbox-Liste steht,
    ///      gelten als veraltet.
    ///   2. Fallback (kein Toolbox / Parse-Fehler):
    ///      Pro Product wird die HÖCHSTE Version als "aktiv" gewertet, alle älteren als veraltet.
    /// </summary>
    private static (List<string> Active, List<string> Obsolete) ClassifyInstallations(IReadOnlyList<ProductDir> productDirs)
    {
        var active = new List<string>();
        var obsolete = new List<string>();

        // Schritt 1: Toolbox-basierte Klassifikation (best effort).
        HashSet<(string Product, string Version)>? toolboxInstalled = null;
        try
        {
            toolboxInstalled = TryReadToolboxInstalledVersions();
        }
        catch
        {
            // bewusst geschluckt — wir fallen unten auf Heuristik zurück
            toolboxInstalled = null;
        }

        if (toolboxInstalled is { Count: > 0 })
        {
            foreach (var pd in productDirs)
            {
                bool isCurrent = toolboxInstalled.Any(t =>
                    string.Equals(t.Product, pd.Product, StringComparison.OrdinalIgnoreCase) &&
                    VersionsMatch(t.Version, pd.Version));
                (isCurrent ? active : obsolete).Add(pd.Path);
            }
            return (active, obsolete);
        }

        // Schritt 2: Heuristik — pro Product die höchste Version aktiv lassen.
        foreach (var group in productDirs.GroupBy(p => p.Product, StringComparer.OrdinalIgnoreCase))
        {
            // ParseVersion liefert ein Tuple-Sort-Key. Höchste Version => aktiv.
            var ordered = group
                .OrderByDescending(p => ParseVersionKey(p.Version))
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                (i == 0 ? active : obsolete).Add(ordered[i].Path);
            }
        }

        return (active, obsolete);
    }

    /// <summary>
    /// Versucht aus dem Toolbox-State die aktuell installierten (Product, Version)-Kombinationen zu lesen.
    /// Bevorzugt Verzeichnisstruktur unter %LOCALAPPDATA%\JetBrains\Toolbox\apps\<AppId>\ch-0\<version>\
    /// (offizielles Layout der Toolbox seit 1.27). Fällt zurück auf Roaming-Settings, falls vorhanden.
    /// Gibt null zurück, wenn keine Toolbox erkennbar ist; leere Menge, wenn Toolbox da, aber keine Apps.
    /// </summary>
    private static HashSet<(string Product, string Version)>? TryReadToolboxInstalledVersions()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var toolboxLocal = Path.Combine(local, "JetBrains", "Toolbox");
        var toolboxAppsDir = Path.Combine(toolboxLocal, "apps");

        if (!Directory.Exists(toolboxLocal))
            return null; // Keine Toolbox -> Fallback auf Heuristik.

        var result = new HashSet<(string, string)>();

        // Primärquelle: Ordnerstruktur unter Toolbox\apps\<AppId>\ — robust gegen JSON-Schema-Änderungen.
        // Layout (typisch): apps\Rider\ch-0\2024.1.5\... oder apps\IDEA-U\ch-0\2024.1.4\...
        // Die <AppId> ist NICHT zwingend identisch mit dem Daten-Ordner-Produktnamen ("IDEA-U" vs "IntelliJIdea"),
        // daher mappen wir AppId -> Produktname.
        if (Directory.Exists(toolboxAppsDir))
        {
            foreach (var appDir in SafeEnumerator.EnumerateDirectories(toolboxAppsDir))
            {
                var appId = Path.GetFileName(appDir);
                var product = MapToolboxAppIdToProduct(appId);
                if (product is null) continue;

                // Versions-Unterordner suchen: entweder direkt unter appDir oder unter "ch-N".
                foreach (var maybeChannel in SafeEnumerator.EnumerateDirectories(appDir))
                {
                    var leaf = Path.GetFileName(maybeChannel);
                    if (leaf.StartsWith("ch-", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var versionDir in SafeEnumerator.EnumerateDirectories(maybeChannel))
                        {
                            var v = Path.GetFileName(versionDir);
                            if (LooksLikeVersion(v))
                                result.Add((product, v));
                        }
                    }
                    else if (LooksLikeVersion(leaf))
                    {
                        result.Add((product, leaf));
                    }
                }
            }
        }

        // Sekundärquelle: Roaming-Settings (.history.json / state.json). Best effort, Schema kann variieren.
        // Wir suchen schlicht nach "version"-Strings in den JSON-Files, ohne uns auf ein festes Schema festzulegen.
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var toolboxRoaming = Path.Combine(roaming, "JetBrains", "Toolbox");
        if (Directory.Exists(toolboxRoaming))
        {
            foreach (var jsonFile in SafeEnumerator.EnumerateFiles(toolboxRoaming, "*.json", recursive: true))
            {
                try
                {
                    var bytes = File.ReadAllBytes(jsonFile);
                    using var doc = JsonDocument.Parse(bytes,
                        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    ExtractToolboxVersionsFromJson(doc.RootElement, result);
                }
                catch
                {
                    // Schema unbekannt / Datei locked / kein JSON — ignorieren, primäre Ordner-Quelle reicht.
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Mappt die Toolbox-interne AppId (z.B. "IDEA-U", "Rider", "PyCharm-P") auf den Produktnamen,
    /// wie er in den Daten-Ordnernamen unter %LOCALAPPDATA%\JetBrains\<Product><Version> erscheint.
    /// </summary>
    private static string? MapToolboxAppIdToProduct(string appId)
    {
        // Diese Mappings basieren auf den dokumentierten Toolbox-AppIds — Schema-Annahme, kann sich ändern.
        return appId.ToUpperInvariant() switch
        {
            "IDEA-U" or "IDEA-EAP" => "IntelliJIdea",
            "IDEA-C" or "IDEA-CE" => "IdeaIC",
            "RIDER" or "RIDER-EAP" => "Rider",
            "PYCHARM-P" or "PYCHARM-PROFESSIONAL" or "PYCHARM-EAP" => "PyCharm",
            "PYCHARM-C" or "PYCHARM-CE" => "PyCharmCE",
            "WEBSTORM" or "WEBSTORM-EAP" => "WebStorm",
            "PHPSTORM" or "PHPSTORM-EAP" => "PhpStorm",
            "GOLAND" or "GOLAND-EAP" => "GoLand",
            "CLION" or "CLION-EAP" => "CLion",
            "RUSTROVER" or "RUSTROVER-EAP" => "RustRover",
            "DATAGRIP" or "DATAGRIP-EAP" => "DataGrip",
            "DATASPELL" or "DATASPELL-EAP" => "DataSpell",
            "RUBYMINE" or "RUBYMINE-EAP" => "RubyMine",
            "APPCODE" => "AppCode",
            "AQUA" or "AQUA-EAP" => "Aqua",
            "WRITERSIDE" or "WRITERSIDE-EAP" => "Writerside",
            "FLEET" => "Fleet",
            "ANDROID-STUDIO" => "AndroidStudio",
            _ => null, // Unbekannte AppId — ignorieren (z.B. Toolbox-eigene Komponenten).
        };
    }

    /// <summary>
    /// Sucht rekursiv in einem JSON-Doc nach "version"-Properties und sammelt sie. Best effort,
    /// kein Verlass auf konkretes Schema. Hilfreich, wenn die Ordner-basierte Quelle aus Toolbox\apps
    /// leer ist (z.B. Toolbox läuft, aber Apps anderswo installiert).
    /// </summary>
    private static void ExtractToolboxVersionsFromJson(JsonElement element, HashSet<(string Product, string Version)> sink)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? productHint = null;
                string? versionHint = null;
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var pn = prop.Name;
                        var v = prop.Value.GetString();
                        if (string.IsNullOrEmpty(v)) continue;
                        if (pn.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                            pn.Equals("installed-version", StringComparison.OrdinalIgnoreCase))
                        {
                            versionHint = v;
                        }
                        else if (pn.Equals("intellij-platform-product-code", StringComparison.OrdinalIgnoreCase) ||
                                 pn.Equals("product-code", StringComparison.OrdinalIgnoreCase) ||
                                 pn.Equals("product", StringComparison.OrdinalIgnoreCase) ||
                                 pn.Equals("tool-id", StringComparison.OrdinalIgnoreCase) ||
                                 pn.Equals("toolId", StringComparison.OrdinalIgnoreCase))
                        {
                            productHint = MapToolboxAppIdToProduct(v) ?? v;
                        }
                    }
                    else
                    {
                        ExtractToolboxVersionsFromJson(prop.Value, sink);
                    }
                }
                if (productHint is not null && versionHint is not null && LooksLikeVersion(versionHint))
                {
                    sink.Add((productHint, versionHint));
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ExtractToolboxVersionsFromJson(item, sink);
                break;
        }
    }

    /// <summary>
    /// Vergleicht zwei Versionsstrings tolerant. Ein Produktordner "Rider2024.1" passt zu Toolbox-Version
    /// "2024.1.5" (Major.Minor stimmt überein) — JetBrains-IDE-Datenordner verwenden nur Major.Minor.
    /// </summary>
    private static bool VersionsMatch(string toolboxVersion, string dataDirVersion)
    {
        if (string.Equals(toolboxVersion, dataDirVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        // Vergleiche Major.Minor.
        var a = SplitVersion(toolboxVersion);
        var b = SplitVersion(dataDirVersion);
        if (a.Length >= 2 && b.Length >= 2)
            return a[0] == b[0] && a[1] == b[1];
        return false;
    }

    private static int[] SplitVersion(string v)
    {
        var parts = v.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n))
                nums.Add(n);
            else
                break;
        }
        return nums.ToArray();
    }

    /// <summary>
    /// Sort-Key für Versionsstrings — höher = neuer. Tuple von bis zu 4 Komponenten.
    /// </summary>
    private static (int, int, int, int) ParseVersionKey(string v)
    {
        var nums = SplitVersion(v);
        return (
            nums.Length > 0 ? nums[0] : 0,
            nums.Length > 1 ? nums[1] : 0,
            nums.Length > 2 ? nums[2] : 0,
            nums.Length > 3 ? nums[3] : 0);
    }

    private static bool LooksLikeVersion(string s) =>
        !string.IsNullOrEmpty(s) && char.IsDigit(s[0]) && s.Contains('.');

    /// <summary>
    /// Liefert Legacy-System-Ordner (vor JetBrains 2020): %USERPROFILE%\.IntelliJIdea2019.3\system etc.
    /// </summary>
    private static IEnumerable<string> EnumerateLegacySystemDirs()
    {
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
