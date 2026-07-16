using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cleaner.Core.Services;

/// <summary>Wie ein Kill-Profil die getroffenen Prozesse beendet.</summary>
public enum KillMode
{
    /// <summary>Höflich: CloseMainWindow (wie Klick aufs X). Prozesse ohne Fenster bleiben stehen.</summary>
    Graceful,

    /// <summary>Mehrstufig wie im Prozess-Monitor: Process.Kill → taskkill /F → taskkill /F /T.</summary>
    Standard,

    /// <summary>Sofort taskkill /F /T (kompletter Prozessbaum, keine Vorstufen).</summary>
    ForceTree,

    /// <summary>So hart und schnell wie möglich: paralleles TerminateProcess (inkl. Baum), kein Warten.</summary>
    Nuke,
}

/// <summary>Ein Prozess, den ein Kill-Profil aktuell treffen würde.</summary>
public sealed class KillMatch
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public string? FilePath { get; init; }
    public long WorkingSetBytes { get; init; }
}

public sealed class KillExecutionResult
{
    public int MatchedCount { get; init; }
    public int KilledCount { get; init; }
    public long FreedBytesEstimate { get; init; }
    public IReadOnlyList<string> Failed { get; init; } = [];
}

public interface IProcessKiller
{
    /// <summary>Alle Prozesse, die die Wildcard-Patterns (z.B. "*chrome*", "node.exe") treffen.</summary>
    IReadOnlyList<KillMatch> FindMatches(IEnumerable<string> patterns);

    /// <summary>Beendet alle Treffer der Patterns im gegebenen Modus.</summary>
    KillExecutionResult Execute(IEnumerable<string> patterns, KillMode mode);
}

/// <summary>
/// Beendet Prozesse anhand von Wildcard-Patterns. Kritische Systemprozesse und der eigene
/// Prozess sind hart geschützt und werden nie getroffen — egal welches Pattern.
/// </summary>
public sealed class ProcessKillerService : IProcessKiller
{
    private readonly IProcessMonitor _monitor;

    public ProcessKillerService(IProcessMonitor monitor) => _monitor = monitor;

    // Ohne diese Prozesse stirbt/blockiert Windows sofort (BSOD oder eingefrorene Session).
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "fontdrvhost", "dwm", "sihost", "audiodg",
    };

    public IReadOnlyList<KillMatch> FindMatches(IEnumerable<string> patterns)
    {
        var cleaned = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var regexes = cleaned.Select(BuildRegex).ToList();
        if (regexes.Count == 0) return [];

        // MainModule-Zugriff ist teuer (Access-Denied-Exceptions bei jedem Systemprozess) —
        // Pfad nur abfragen, wenn überhaupt ein pfadartiges Pattern dabei ist.
        var needsPath = cleaned.Any(p => p.Contains('\\') || p.Contains('/'));

        var ownPid = Environment.ProcessId;
        var result = new List<KillMatch>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == ownPid || Protected.Contains(p.ProcessName)) continue;

                string? path = null;
                if (needsPath)
                    try { path = p.MainModule?.FileName; } catch { /* Access denied ist normal */ }

                if (!regexes.Any(r => IsMatch(r, p.ProcessName, path))) continue;

                result.Add(new KillMatch
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    FilePath = path,
                    WorkingSetBytes = SafeWorkingSet(p),
                });
            }
            catch { /* zombie */ }
            finally { p.Dispose(); }
        }
        return result.OrderByDescending(m => m.WorkingSetBytes).ToList();
    }

    public KillExecutionResult Execute(IEnumerable<string> patterns, KillMode mode)
    {
        var matches = FindMatches(patterns);
        var failed = new List<string>();
        long freed = 0;
        int killed = 0;

        if (mode == KillMode.Nuke)
        {
            // Maximal schnell: alle Treffer parallel terminieren, nirgends warten.
            Parallel.ForEach(matches, m =>
            {
                try
                {
                    using var p = Process.GetProcessById(m.Pid);
                    p.Kill(entireProcessTree: true);
                }
                catch { /* schon weg oder Access denied — unten verifizieren */ }
            });
        }
        else
        {
            foreach (var m in matches)
            {
                try
                {
                    switch (mode)
                    {
                        case KillMode.Graceful:
                            using (var p = Process.GetProcessById(m.Pid))
                                p.CloseMainWindow();
                            break;
                        case KillMode.ForceTree:
                            ProcessMonitorService.RunTaskkill($"/F /T /PID {m.Pid}", 3000);
                            break;
                        default:
                            _monitor.Kill(m.Pid);
                            break;
                    }
                }
                catch { /* unten verifizieren */ }
            }
        }

        // Kurze Verifikation (Graceful braucht Zeit — dort zählt der Versuch)
        foreach (var m in matches)
        {
            if (mode == KillMode.Graceful || !ProcessMonitorService.IsAlive(m.Pid))
            {
                killed++;
                freed += m.WorkingSetBytes;
            }
            else
            {
                failed.Add($"{m.Name} (PID {m.Pid})");
            }
        }

        return new KillExecutionResult
        {
            MatchedCount = matches.Count,
            KilledCount = killed,
            FreedBytesEstimate = freed,
            Failed = failed,
        };
    }

    private static long SafeWorkingSet(Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }

    /// <summary>Wildcard → Regex ("*chrome*" → ^.*chrome.*$), case-insensitive.</summary>
    public static Regex BuildRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Trim())
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Match gegen Prozessname, "name.exe" und (bei Pfad-Patterns) den vollen Pfad —
    // so treffen "*chrome*", "chrome.exe" und "C:\Tools\*" gleichermaßen.
    private static bool IsMatch(Regex regex, string processName, string? filePath)
    {
        if (regex.IsMatch(processName) || regex.IsMatch(processName + ".exe")) return true;
        return filePath != null && regex.IsMatch(filePath);
    }
}
