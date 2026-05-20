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

    public bool Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: false);
            return p.WaitForExit(3000);
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
