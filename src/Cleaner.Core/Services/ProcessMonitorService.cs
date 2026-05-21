using System.Diagnostics;

namespace Cleaner.Core.Services;

public sealed class ProcessSnapshot
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? FilePath { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateMemoryBytes { get; init; }
    public required TimeSpan TotalCpuTime { get; init; }
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required DateTime StartTimeUtc { get; init; }
    public double CpuPercent { get; set; }
    public bool IsCurrentUser { get; init; }
}

public interface IProcessMonitor
{
    IReadOnlyList<ProcessSnapshot> Snapshot();
    bool Kill(int pid);
    bool KillElevated(int pid);
    bool OpenFileLocation(int pid);
}

public sealed class ProcessMonitorService : IProcessMonitor
{
    private readonly Dictionary<int, (TimeSpan cpu, DateTime when)> _lastSamples = new();

    public IReadOnlyList<ProcessSnapshot> Snapshot()
    {
        var now = DateTime.UtcNow;
        var processes = Process.GetProcesses();
        var list = new List<ProcessSnapshot>(processes.Length);

        foreach (var p in processes)
        {
            try
            {
                string? path = null;
                string? desc = null;
                bool isMine = false;

                try { path = p.MainModule?.FileName; } catch { /* Access denied common */ }
                try { desc = p.MainModule?.FileVersionInfo?.FileDescription; } catch { }
                try { isMine = p.SessionId != 0; } catch { }

                var cpu = p.TotalProcessorTime;

                double cpuPercent = 0;
                if (_lastSamples.TryGetValue(p.Id, out var last))
                {
                    var deltaCpu = (cpu - last.cpu).TotalMilliseconds;
                    var deltaWall = (now - last.when).TotalMilliseconds;
                    if (deltaWall > 0)
                        cpuPercent = Math.Max(0, deltaCpu / deltaWall / Environment.ProcessorCount * 100);
                }
                _lastSamples[p.Id] = (cpu, now);

                list.Add(new ProcessSnapshot
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    Description = desc,
                    FilePath = path,
                    WorkingSetBytes = p.WorkingSet64,
                    PrivateMemoryBytes = p.PrivateMemorySize64,
                    TotalCpuTime = cpu,
                    ThreadCount = p.Threads.Count,
                    HandleCount = p.HandleCount,
                    StartTimeUtc = SafeStartTime(p),
                    CpuPercent = cpuPercent,
                    IsCurrentUser = isMine,
                });
            }
            catch { /* zombie process */ }
            finally { p.Dispose(); }
        }

        // alte Samples aufräumen
        var alive = list.Select(p => p.Pid).ToHashSet();
        foreach (var key in _lastSamples.Keys.ToList())
            if (!alive.Contains(key)) _lastSamples.Remove(key);

        return list
            .OrderByDescending(p => p.WorkingSetBytes)
            .ToList();
    }

    private static DateTime SafeStartTime(Process p)
    {
        try { return p.StartTime.ToUniversalTime(); }
        catch { return DateTime.MinValue; }
    }

    /// <summary>
    /// Mehrstufiger Kill: erst .NET-Process.Kill, dann taskkill /F, dann taskkill /F /T.
    /// taskkill nutzt direkt TerminateProcess auf NT-Ebene und schafft oft mehr als der
    /// managed-Wrapper (z.B. bei Processes die WM_CLOSE ignorieren oder mehrere Threads
    /// blockieren).
    /// </summary>
    public bool Kill(int pid)
    {
        // 1. Standard-Kill versuchen
        try
        {
            using var p = Process.GetProcessById(pid);
            try { p.Kill(entireProcessTree: false); } catch { /* Access denied — taskkill probieren */ }
            if (p.WaitForExit(2000)) return true;
        }
        catch (ArgumentException) { return true; /* Process bereits weg */ }
        catch { /* weitermachen mit taskkill */ }

        if (!IsAlive(pid)) return true;

        // 2. taskkill /F /PID
        if (RunTaskkill($"/F /PID {pid}", 2000) && !IsAlive(pid)) return true;

        // 3. taskkill /F /T /PID (entire tree)
        if (RunTaskkill($"/F /T /PID {pid}", 2000) && !IsAlive(pid)) return true;

        return !IsAlive(pid);
    }

    /// <summary>
    /// Erzwingt eine UAC-Elevation und führt taskkill als Admin aus. Sinnvoll wenn der
    /// normale Kill an Access-Denied scheitert (z.B. bei Processes anderer User oder
    /// services-internen Workern).
    /// </summary>
    public bool KillElevated(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/F /T /PID {pid}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var elevated = Process.Start(psi);
            if (elevated is null) return false;
            elevated.WaitForExit(5000);
            return !IsAlive(pid);
        }
        catch { return false; }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch { return true; }
    }

    private static bool RunTaskkill(string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            return proc.WaitForExit(timeoutMs);
        }
        catch { return false; }
    }

    public bool OpenFileLocation(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var path = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) return false;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
