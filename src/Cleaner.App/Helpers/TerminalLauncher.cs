using System.Diagnostics;
using System.IO;

namespace Cleaner.App.Helpers;

/// <summary>
/// Öffnet eine Terminal-Session im angegebenen Verzeichnis. Windows Terminal (wt.exe)
/// IGNORIERT ProcessStartInfo.WorkingDirectory, deshalb müssen wir den Pfad per
/// '-d "..."' explizit übergeben. powershell/cmd respektieren WorkingDirectory.
/// </summary>
public static class TerminalLauncher
{
    public static bool OpenIn(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;

        // 1. Windows Terminal — braucht -d
        if (TryStart("wt.exe", $"-d \"{directory}\"", workingDir: null)) return true;

        // 2. PowerShell 7+ falls vorhanden
        if (TryStart("pwsh.exe", null, workingDir: directory)) return true;

        // 3. Windows PowerShell
        if (TryStart("powershell.exe", null, workingDir: directory)) return true;

        // 4. cmd
        if (TryStart("cmd.exe", $"/K cd /d \"{directory}\"", workingDir: null)) return true;

        App.LogInfo($"TerminalLauncher: kein Shell konnte gestartet werden für {directory}");
        return false;
    }

    private static bool TryStart(string exe, string? args, string? workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
            };
            if (args is not null) psi.Arguments = args;
            if (workingDir is not null) psi.WorkingDirectory = workingDir;

            using var proc = Process.Start(psi);
            return proc is not null;
        }
        catch (Exception ex)
        {
            App.LogInfo($"TerminalLauncher.TryStart({exe}) fail: {ex.Message}");
            return false;
        }
    }
}
