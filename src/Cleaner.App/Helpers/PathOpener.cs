using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Cleaner.App.Helpers;

/// <summary>
/// Zentrale Stelle für alle "öffne X in Windows"-Aktionen. Setzt auf Shell-Verben statt
/// auf das fragile IContextMenu-COM-Interface.
/// </summary>
public static class PathOpener
{
    /// <summary>Im Explorer mit Datei selektiert / Ordner öffnen.</summary>
    public static bool RevealInExplorer(string? rawPathOrCommand)
    {
        var path = Resolve(rawPathOrCommand);
        if (path is null)
        {
            Cleaner.App.App.LogInfo($"RevealInExplorer: Pfad konnte nicht aufgelöst werden — {rawPathOrCommand}");
            MessageBox.Show($"Pfad konnte nicht gefunden werden:\n\n{rawPathOrCommand}",
                "Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else return false;
            return true;
        }
        catch (Exception ex) { Cleaner.App.App.LogException("RevealInExplorer", ex); return false; }
    }

    /// <summary>Öffnet den enthaltenden Ordner (auch wenn der Pfad selbst nicht existiert, wenn der Parent existiert).</summary>
    public static bool OpenContainingFolder(string? rawPathOrCommand)
    {
        var path = Resolve(rawPathOrCommand);
        if (path is null) return false;
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return true;
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return true;
            }
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                return true;
            }
            return false;
        }
        catch (Exception ex) { Cleaner.App.App.LogException("OpenContainingFolder", ex); return false; }
    }

    /// <summary>Windows "Öffnen mit..."-Dialog (= shell-verb 'openas').</summary>
    public static bool OpenWithDialog(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Verb = "openas",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) { Cleaner.App.App.LogException("OpenWithDialog", ex); return false; }
    }

    public static bool OpenDefault(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { Cleaner.App.App.LogException("OpenDefault", ex); return false; }
    }

    /// <summary>
    /// Windows-Eigenschaften-Dialog für Datei oder Ordner. Process.Start mit Verb="properties"
    /// ist unzuverlässig (Dialog schließt sich oft sofort wieder weil er an den startenden
    /// Process gebunden ist). SHObjectProperties öffnet den Dialog stabil — modal zum
    /// aufrufenden Process, so wie der Explorer das macht.
    /// </summary>
    public static bool ShowProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Cleaner.App.App.LogInfo($"ShowProperties: Pfad existiert nicht — {path}");
            return false;
        }

        try
        {
            // SHOP_FILEPATH = 0x2 → wir übergeben einen Pfad-String (kein PIDL).
            bool ok = SHObjectProperties(IntPtr.Zero, SHOP_FILEPATH, path, null);
            if (!ok) Cleaner.App.App.LogInfo($"ShowProperties: SHObjectProperties returned false für {path}");
            return ok;
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("ShowProperties", ex);
            return false;
        }
    }

    const uint SHOP_FILEPATH = 0x00000002;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType,
        [MarshalAs(UnmanagedType.LPWStr)] string pszObjectName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszPropertyPage);

    /// <summary>Als Administrator ausführen (Shell-Verb 'runas') — nur sinnvoll für ausführbare Dateien.</summary>
    public static bool RunAsAdmin(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Verb = "runas",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) { Cleaner.App.App.LogException("RunAsAdmin", ex); return false; }
    }

    /// <summary>
    /// Versucht aus einer Command-Line (z.B. <c>"C:\foo\bar.exe" --arg1</c>) den ausführbaren
    /// Pfad zu extrahieren. Behandelt auch Treiber-Pfade (<c>\??\</c>, <c>\SystemRoot\</c>).
    /// </summary>
    public static string? Resolve(string? rawPathOrCommand)
    {
        if (string.IsNullOrWhiteSpace(rawPathOrCommand)) return null;
        var s = rawPathOrCommand.Trim();

        // NT-Pfad-Präfixe normalisieren
        if (s.StartsWith(@"\??\", StringComparison.Ordinal)) s = s[4..];
        if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            s = Path.Combine(win, s[12..]);
        }

        // Direktes Ergebnis
        if (File.Exists(s) || Directory.Exists(s)) return s;

        // Quoted: "C:\path\with space\foo.exe" --arg
        if (s.StartsWith("\""))
        {
            int close = s.IndexOf('"', 1);
            if (close > 0)
            {
                var quoted = s[1..close];
                if (File.Exists(quoted) || Directory.Exists(quoted)) return quoted;
                // Komma-getrennte Variante (rundll32 etc)
                int comma = quoted.IndexOf(',');
                if (comma > 0)
                {
                    var trimmed = quoted[..comma];
                    if (File.Exists(trimmed)) return trimmed;
                }
            }
        }

        // Unquoted: extrahiere bis zum ersten Whitespace, dann progressiv probieren
        int firstSpace = s.IndexOf(' ');
        if (firstSpace > 0)
        {
            var candidate = s[..firstSpace];
            if (File.Exists(candidate)) return candidate;
            if (File.Exists(candidate + ".exe")) return candidate + ".exe";
        }

        // Rundll32-Format: "...\rundll32.exe,VerbName" → komma-trim
        int commaIdx = s.IndexOf(',');
        if (commaIdx > 0)
        {
            var trimmed = s[..commaIdx];
            if (File.Exists(trimmed)) return trimmed;
        }

        // Letzter Versuch: env-vars expandieren
        var expanded = Environment.ExpandEnvironmentVariables(s);
        if (File.Exists(expanded) || Directory.Exists(expanded)) return expanded;

        return null;
    }

    public static bool CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception ex) { Cleaner.App.App.LogException("CopyToClipboard", ex); return false; }
    }
}
