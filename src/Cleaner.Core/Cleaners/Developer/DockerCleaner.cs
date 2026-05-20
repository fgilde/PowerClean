using System.Diagnostics;
using System.Text.Json;
using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Developer;

/// <summary>
/// Wrapper um `docker system df` / `docker system prune`. Räumt nur unused Container, Images,
/// Networks und (optional) Volumes weg. Auf Systemen ohne Docker komplett unsichtbar.
/// </summary>
public sealed class DockerCleaner : ICleanupTarget
{
    private readonly IFileSystemOperations _fs;

    public DockerCleaner(IFileSystemOperations fs) { _fs = fs; }

    public string Id => "dev.docker";
    public string Name => "Docker (unused containers, images, networks)";
    public string Description =>
        "Führt `docker system prune` aus: löscht gestoppte Container, dangling Images und unbenutzte Netzwerke. " +
        "Volumes werden bewusst NICHT angefasst (Datenverlust-Risiko).";
    public string IconGlyph => "";
    public CleanupCategory Category => CleanupCategory.Developer;
    public SafetyLevel SafetyLevel => SafetyLevel.Caution;
    public bool RequiresAdmin => false;

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        long reclaimable = 0;
        int count = 0;
        var lines = new List<string>();

        // docker system df --format json (eine Zeile pro Type)
        var output = await RunDockerAsync("system df --format json", ct);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("Reclaimable", out var rec))
                {
                    var recStr = rec.GetString() ?? "0B";
                    reclaimable += ParseDockerSize(recStr);
                }

                if (root.TryGetProperty("Type", out var t))
                    lines.Add($"{t.GetString()}: {rec}");

                count++;
            }
            catch { /* ignore parse errors */ }
        }

        return new ScanResult
        {
            TargetId = Id,
            SizeBytes = reclaimable,
            FileCount = count,
            // Wir kodieren in Paths die docker-Typen, damit Clean weiß was zu prunen ist
            Paths = lines,
        };
    }

    public async Task<CleanResult> CleanAsync(
        ScanResult scan,
        bool useRecycleBin,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default)
    {
        // -f = ohne Bestätigung. KEIN --volumes (wäre destruktiv).
        var output = await RunDockerAsync("system prune -f", ct);

        long freed = 0;
        // letzte Zeile von prune: "Total reclaimed space: 1.234GB"
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("Total reclaimed space:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.LastIndexOf(':');
                if (idx > 0)
                    freed = ParseDockerSize(line[(idx + 1)..].Trim());
            }
        }

        return new CleanResult
        {
            TargetId = Id,
            FreedBytes = freed,
            FilesDeleted = freed > 0 ? 1 : 0,
        };
    }

    private static async Task<string> RunDockerAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("docker", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi);
        if (p == null) return string.Empty;

        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return stdout;
    }

    private static long ParseDockerSize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        raw = raw.Trim();

        // Format: "1.23GB", "456MB", "789kB", "12B"
        int i = 0;
        while (i < raw.Length && (char.IsDigit(raw[i]) || raw[i] == '.' || raw[i] == ',')) i++;
        var num = raw[..i].Replace(',', '.');
        var unit = raw[i..].Trim().ToUpperInvariant();

        if (!double.TryParse(num, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var val))
            return 0;

        return unit switch
        {
            "B"  => (long)val,
            "KB" => (long)(val * 1024),
            "MB" => (long)(val * 1024 * 1024),
            "GB" => (long)(val * 1024 * 1024 * 1024),
            "TB" => (long)(val * 1024L * 1024 * 1024 * 1024),
            _    => 0,
        };
    }
}
