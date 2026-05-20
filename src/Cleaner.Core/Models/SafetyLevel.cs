namespace Cleaner.Core.Models;

/// <summary>
/// Risiko-Stufe für einen Cleanup-Eintrag. Steuert UI-Farbe und Default-Auswahl.
/// </summary>
public enum SafetyLevel
{
    /// <summary>Komplett sicher zu löschen, wird automatisch neu erstellt (z.B. Thumbnail-Cache).</summary>
    Safe = 0,

    /// <summary>Empfohlen zu löschen, Auswirkungen minimal (z.B. Browser-Cache: nur längere Ladezeiten).</summary>
    Recommended = 1,

    /// <summary>Vorsicht: kann Re-Logins erfordern, Einstellungen zurücksetzen oder lange Neuaufbau-Zeit (IDE-Caches).</summary>
    Caution = 2,

    /// <summary>Warnung: irreversible Konsequenzen möglich (Docker-Volumes mit Daten, Logs, Memory-Dumps).</summary>
    Warning = 3,
}
