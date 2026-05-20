using Cleaner.Core.Models;

namespace Cleaner.Core.Cleaners;

/// <summary>
/// Repräsentiert eine aufräumbare Datenquelle. Implementierungen müssen idempotent und
/// safe-by-default sein: nur das löschen, was sie wirklich kennen.
/// </summary>
public interface ICleanupTarget
{
    /// <summary>Stabiler Identifier (für Settings / Telemetrie).</summary>
    string Id { get; }

    string Name { get; }
    string Description { get; }
    string IconGlyph { get; }
    CleanupCategory Category { get; }
    SafetyLevel SafetyLevel { get; }

    /// <summary>True, wenn der Cleaner Admin-Rechte zum Löschen braucht (z.B. C:\Windows\Temp).</summary>
    bool RequiresAdmin { get; }

    /// <summary>True, wenn der Cleaner auf dem aktuellen System verfügbar/anwendbar ist.</summary>
    bool IsAvailable();

    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<CleanResult> CleanAsync(
        ScanResult scan,
        bool useRecycleBin,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
