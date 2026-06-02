using System.IO;

namespace Cleaner.App.Services;

/// <summary>
/// Wiederherstellung von Dateien aus dem Windows-Papierkorb über die Shell-Automation
/// (Shell.Application, spät gebunden via COM). Matching erfolgt über den ursprünglichen
/// vollständigen Pfad. Nur möglich, solange die Datei noch im Papierkorb liegt.
/// </summary>
public sealed class RecycleBinService
{
    private const int SsfBitBucket = 10; // Papierkorb-Namespace

    // Verb-Namen für "Wiederherstellen" in verschiedenen Sprachen (ohne '&').
    private static readonly string[] RestoreVerbs =
    {
        "wiederherstellen", "restore", "estore", "restaurer", "ripristina", "restaurar",
    };

    /// <summary>
    /// Stellt alle Papierkorb-Einträge wieder her, deren Ursprungspfad in <paramref name="originalPaths"/> liegt.
    /// Liefert die Anzahl erfolgreich wiederhergestellter Dateien.
    /// </summary>
    public int RestoreByOriginalPaths(IEnumerable<string> originalPaths)
    {
        var wanted = new HashSet<string>(
            originalPaths.Where(p => !string.IsNullOrWhiteSpace(p)),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return 0;

        int restored = 0;
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return 0;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return 0;

            dynamic bin = shell.NameSpace(SsfBitBucket);
            if (bin is null) return 0;

            dynamic items = bin.Items();
            int count = items.Count;

            // Spalte mit dem Ursprungspfad ermitteln (sprachabhängig) — Header durchsuchen.
            int origColumn = FindOriginalLocationColumn(bin);

            // Rückwärts iterieren: Wiederherstellen entfernt Items aus der Collection.
            for (int i = count - 1; i >= 0; i--)
            {
                dynamic item = items.Item(i);
                if (item is null) continue;

                string name = item.Name;
                string folder = origColumn >= 0 ? (string)bin.GetDetailsOf(item, origColumn) : string.Empty;
                if (string.IsNullOrEmpty(folder)) continue;

                string full = Path.Combine(folder, name);
                if (!wanted.Contains(full)) continue;

                if (InvokeRestore(item)) restored++;
            }
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RecycleBin.Restore", ex);
        }
        finally
        {
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }

        return restored;
    }

    /// <summary>Öffnet den Papierkorb im Explorer.</summary>
    public static void OpenRecycleBin()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RecycleBin.Open", ex);
        }
    }

    private static int FindOriginalLocationColumn(dynamic bin)
    {
        // Bekannte Header-Bezeichnungen für die Ursprungsspalte.
        string[] headers = { "ursprünglicher pfad", "ursprung", "original location", "origine", "percorso originale" };
        for (int col = 0; col < 10; col++)
        {
            try
            {
                string header = ((string)bin.GetDetailsOf(null, col) ?? string.Empty).Trim().ToLowerInvariant();
                if (headers.Any(h => header.Contains(h))) return col;
            }
            catch { /* Spalte existiert nicht */ }
        }
        // Fallback: Spalte 1 ist auf Win10/11 üblicherweise der Ursprungsort.
        return 1;
    }

    private static bool InvokeRestore(dynamic item)
    {
        try
        {
            dynamic verbs = item.Verbs();
            int vc = verbs.Count;
            for (int v = 0; v < vc; v++)
            {
                dynamic verb = verbs.Item(v);
                string name = ((string)verb.Name ?? string.Empty).Replace("&", string.Empty).Trim().ToLowerInvariant();
                if (RestoreVerbs.Any(rv => name == rv || name.StartsWith(rv)))
                {
                    verb.DoIt();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RecycleBin.InvokeRestore", ex);
        }
        return false;
    }
}
