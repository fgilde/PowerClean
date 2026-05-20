using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public enum AutostartSource
{
    HklmRun,
    HkcuRun,
    HklmRunOnce,
    HkcuRunOnce,
    HklmRun32,
    HkcuRun32,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTask,
    Service,
}

public sealed class AutostartEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required AutostartSource Source { get; init; }
    public bool IsEnabled { get; set; }
    public string? Publisher { get; init; }
    public string? Description { get; init; }

    /// <summary>Wo der Eintrag gespeichert ist (Registry-Pfad oder Datei-Pfad). Wird zum Löschen/Disable gebraucht.</summary>
    public required string Location { get; init; }

    public string SourceLabel => Source switch
    {
        AutostartSource.HklmRun           => @"HKLM\...\Run",
        AutostartSource.HkcuRun           => @"HKCU\...\Run",
        AutostartSource.HklmRunOnce       => @"HKLM\...\RunOnce",
        AutostartSource.HkcuRunOnce       => @"HKCU\...\RunOnce",
        AutostartSource.HklmRun32         => @"HKLM\...\Wow6432\Run",
        AutostartSource.HkcuRun32         => @"HKCU\...\Wow6432\Run",
        AutostartSource.StartupFolderUser => "Startup-Ordner (User)",
        AutostartSource.StartupFolderCommon => "Startup-Ordner (Alle)",
        AutostartSource.ScheduledTask     => "Aufgabenplanung",
        AutostartSource.Service           => "Dienst (Automatisch)",
        _ => Source.ToString(),
    };
}

public interface IAutostartScanner
{
    Task<IReadOnlyList<AutostartEntry>> ScanAsync(CancellationToken ct = default);
    bool ToggleEnabled(AutostartEntry entry, bool enabled);
    bool Delete(AutostartEntry entry);
}

public sealed class AutostartScanner : IAutostartScanner
{
    public async Task<IReadOnlyList<AutostartEntry>> ScanAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<AutostartEntry>();
            list.AddRange(ScanRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run",
                AutostartSource.HklmRun));
            list.AddRange(ScanRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                AutostartSource.HklmRunOnce));
            list.AddRange(ScanRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run",
                AutostartSource.HkcuRun));
            list.AddRange(ScanRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                AutostartSource.HkcuRunOnce));

            if (Environment.Is64BitOperatingSystem)
            {
                list.AddRange(ScanRegistry(Registry.LocalMachine, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                    AutostartSource.HklmRun32));
                list.AddRange(ScanRegistry(Registry.CurrentUser, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                    AutostartSource.HkcuRun32));
            }

            list.AddRange(ScanStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                AutostartSource.StartupFolderUser));
            list.AddRange(ScanStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                AutostartSource.StartupFolderCommon));

            try { list.AddRange(ScanScheduledTasks(ct)); } catch { /* schtasks evtl. nicht da */ }
            try { list.AddRange(ScanAutoServices()); } catch { /* fallback */ }

            return (IReadOnlyList<AutostartEntry>)list
                .OrderBy(e => e.Source)
                .ThenBy(e => e.Name)
                .ToList();
        }, ct);
    }

    private static IEnumerable<AutostartEntry> ScanRegistry(RegistryKey root, string subPath, AutostartSource source)
    {
        using var key = root.OpenSubKey(subPath, writable: false);
        if (key is null) yield break;

        // Disabled-Tracking: korrespondierender ApprovedKey
        var approvedPath = subPath.Replace(@"CurrentVersion\Run", @"CurrentVersion\Explorer\StartupApproved\Run", StringComparison.OrdinalIgnoreCase)
                                  .Replace(@"CurrentVersion\RunOnce", @"CurrentVersion\Explorer\StartupApproved\RunOnce", StringComparison.OrdinalIgnoreCase);
        using var approved = root.OpenSubKey(approvedPath, writable: false);

        foreach (var name in key.GetValueNames())
        {
            string command = key.GetValue(name)?.ToString() ?? "";
            bool enabled = true;
            if (approved?.GetValue(name) is byte[] data && data.Length > 0)
                enabled = data[0] == 0x02; // 0x02 enabled, 0x03 disabled

            yield return new AutostartEntry
            {
                Name = name,
                Command = command,
                Source = source,
                IsEnabled = enabled,
                Location = $@"{root.Name}\{subPath}\{name}",
            };
        }
    }

    private static IEnumerable<AutostartEntry> ScanStartupFolder(string path, AutostartSource source)
    {
        if (!Directory.Exists(path)) yield break;
        foreach (var file in Directory.EnumerateFiles(path))
        {
            yield return new AutostartEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Command = file,
                Source = source,
                IsEnabled = true,
                Location = file,
            };
        }
    }

    private static IEnumerable<AutostartEntry> ScanScheduledTasks(CancellationToken ct)
    {
        // schtasks /query /fo csv /v gibt alle Tasks aus, inkl. Trigger.
        // Wir filtern auf "logon"/"boot"-Trigger und Tasks die nicht von MS sind.
        var psi = new ProcessStartInfo("schtasks.exe", "/query /fo csv /v")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        using var p = Process.Start(psi);
        if (p is null) yield break;

        string? line;
        string[]? headers = null;
        var entries = new List<AutostartEntry>();
        while ((line = p.StandardOutput.ReadLine()) != null)
        {
            if (ct.IsCancellationRequested) break;
            var cols = ParseCsvLine(line);
            if (cols.Length < 5) continue;
            if (headers is null) { headers = cols; continue; }
            if (cols[0] == headers[0]) continue; // Header-Wiederholung

            int idxTask = Array.FindIndex(headers, h => h.Equals("TaskName", StringComparison.OrdinalIgnoreCase));
            int idxState = Array.FindIndex(headers, h => h.Equals("Status", StringComparison.OrdinalIgnoreCase));
            int idxRun = Array.FindIndex(headers, h => h.Equals("Task To Run", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Equals("Auszuführende Aufgabe", StringComparison.OrdinalIgnoreCase));
            int idxAuthor = Array.FindIndex(headers, h => h.Equals("Author", StringComparison.OrdinalIgnoreCase));
            int idxTrigger = Array.FindIndex(headers, h => h.StartsWith("Schedule Type", StringComparison.OrdinalIgnoreCase) ||
                                                            h.StartsWith("Zeitplantyp", StringComparison.OrdinalIgnoreCase));

            if (idxTask < 0 || idxTask >= cols.Length) continue;
            var taskName = cols[idxTask];
            var run = idxRun >= 0 && idxRun < cols.Length ? cols[idxRun] : "";
            var state = idxState >= 0 && idxState < cols.Length ? cols[idxState] : "";
            var author = idxAuthor >= 0 && idxAuthor < cols.Length ? cols[idxAuthor] : "";
            var trigger = idxTrigger >= 0 && idxTrigger < cols.Length ? cols[idxTrigger] : "";

            // Nur Tasks mit Logon/Boot/Onstart-Triggern
            if (!trigger.Contains("Logon", StringComparison.OrdinalIgnoreCase) &&
                !trigger.Contains("Anmeldung", StringComparison.OrdinalIgnoreCase) &&
                !trigger.Contains("Boot", StringComparison.OrdinalIgnoreCase) &&
                !trigger.Contains("Start", StringComparison.OrdinalIgnoreCase))
                continue;

            // Microsoft-Tasks ausblenden — zu viele und User soll die meist nicht anfassen
            if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;

            entries.Add(new AutostartEntry
            {
                Name = taskName.TrimStart('\\'),
                Command = run,
                Source = AutostartSource.ScheduledTask,
                IsEnabled = !state.Contains("Deaktiviert", StringComparison.OrdinalIgnoreCase) &&
                            !state.Equals("Disabled", StringComparison.OrdinalIgnoreCase),
                Publisher = author,
                Location = taskName,
            });
        }
        p.WaitForExit(5000);
        foreach (var e in entries) yield return e;
    }

    private static IEnumerable<AutostartEntry> ScanAutoServices()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable: false);
        if (key is null) yield break;

        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name, writable: false);
            if (sub is null) continue;

            int startType = sub.GetValue("Start") is int s ? s : -1;
            // 2 = Auto, 0 = Boot, 1 = System. Wir zeigen nur Auto-Start-Dienste.
            if (startType != 2) continue;

            var displayName = sub.GetValue("DisplayName")?.ToString() ?? name;
            var imagePath = sub.GetValue("ImagePath")?.ToString() ?? "";
            var description = sub.GetValue("Description")?.ToString();

            // Microsoft-eigene Standard-Dienste skippen (Heuristik)
            if (imagePath.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase) ||
                imagePath.Contains(@"\SysWOW64\", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new AutostartEntry
            {
                Name = displayName,
                Command = imagePath,
                Source = AutostartSource.Service,
                IsEnabled = true,
                Description = description,
                Location = name,
            };
        }
    }

    public bool ToggleEnabled(AutostartEntry entry, bool enabled)
    {
        try
        {
            switch (entry.Source)
            {
                case AutostartSource.HklmRun: return ToggleRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", entry.Name, enabled);
                case AutostartSource.HkcuRun: return ToggleRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", entry.Name, enabled);
                case AutostartSource.HklmRunOnce: return ToggleRegistry(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\RunOnce", entry.Name, enabled);
                case AutostartSource.HkcuRunOnce: return ToggleRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\RunOnce", entry.Name, enabled);
                case AutostartSource.HklmRun32: return ToggleRegistry(Registry.LocalMachine, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", entry.Name, enabled);
                case AutostartSource.HkcuRun32: return ToggleRegistry(Registry.CurrentUser, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", entry.Name, enabled);
                case AutostartSource.ScheduledTask: return RunSchTasks($"/Change /TN \"{entry.Location}\" {(enabled ? "/Enable" : "/Disable")}");
                default: return false; // Startup-Folder: kein Disable-Konzept, nur Delete
            }
        }
        catch { return false; }
    }

    private static bool ToggleRegistry(RegistryKey root, string approvedPath, string name, bool enabled)
    {
        using var key = root.CreateSubKey(approvedPath, writable: true);
        if (key is null) return false;
        // 0x02 = enabled, 0x03 = disabled. 11 bytes — Rest sind Zeitstempel die wir auf 0 setzen.
        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
        key.SetValue(name, bytes, RegistryValueKind.Binary);
        return true;
    }

    public bool Delete(AutostartEntry entry)
    {
        try
        {
            switch (entry.Source)
            {
                case AutostartSource.HklmRun:
                case AutostartSource.HklmRunOnce:
                case AutostartSource.HklmRun32:
                    return DeleteRegValue(Registry.LocalMachine, RegPathFor(entry.Source), entry.Name);
                case AutostartSource.HkcuRun:
                case AutostartSource.HkcuRunOnce:
                case AutostartSource.HkcuRun32:
                    return DeleteRegValue(Registry.CurrentUser, RegPathFor(entry.Source), entry.Name);
                case AutostartSource.StartupFolderUser:
                case AutostartSource.StartupFolderCommon:
                    File.Delete(entry.Location);
                    return true;
                case AutostartSource.ScheduledTask:
                    return RunSchTasks($"/Delete /TN \"{entry.Location}\" /F");
                default: return false;
            }
        }
        catch { return false; }
    }

    private static string RegPathFor(AutostartSource s) => s switch
    {
        AutostartSource.HklmRun or AutostartSource.HkcuRun => @"Software\Microsoft\Windows\CurrentVersion\Run",
        AutostartSource.HklmRunOnce or AutostartSource.HkcuRunOnce => @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        AutostartSource.HklmRun32 or AutostartSource.HkcuRun32 => @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
        _ => "",
    };

    private static bool DeleteRegValue(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path, writable: true);
        if (key is null) return false;
        key.DeleteValue(name, throwOnMissingValue: false);
        return true;
    }

    private static bool RunSchTasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(5000);
        return p.ExitCode == 0;
    }

    // Minimaler CSV-Parser — schtasks-Output ist quoted und enthält Kommas in Werten
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(cur.ToString());
                cur.Clear();
            }
            else cur.Append(c);
        }
        fields.Add(cur.ToString());
        return fields.ToArray();
    }
}
