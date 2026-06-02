namespace Cleaner.Core.Cleaners;

/// <summary>
/// App-weite Schutzregeln, die für ALLE Cleaner gelten. Wird beim Start und bei
/// Einstellungs-Änderungen aus den Benutzereinstellungen befüllt. Greift in
/// <see cref="CleanupTargetBase"/> sowohl beim Scannen als auch (defensiv) beim Löschen.
/// </summary>
public static class GlobalCleanupPolicy
{
    /// <summary>Pfade, die einen dieser Substrings enthalten, werden nie angefasst (case-insensitive).</summary>
    public static IReadOnlyList<string> ExcludeSubstrings { get; set; } = Array.Empty<string>();

    /// <summary>Wenn gesetzt: nur Dateien löschen, die älter als dieser Zeitraum sind.</summary>
    public static TimeSpan? MinimumAge { get; set; }

    /// <summary>True, wenn der Pfad durch eine globale Schutzregel ausgeschlossen ist.</summary>
    public static bool IsExcluded(string path)
    {
        var ex = ExcludeSubstrings;
        for (int i = 0; i < ex.Count; i++)
        {
            if (!string.IsNullOrEmpty(ex[i]) && path.Contains(ex[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
