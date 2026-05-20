using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public sealed class RestorePoint
{
    public required long SequenceNumber { get; init; }
    public required DateTime CreationTimeUtc { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required long SizeBytes { get; init; }
    public required string Drive { get; init; }
}

public interface ISystemMaintenanceService
{
    bool IsHibernationEnabled();
    long GetHibernationFileSize();
    long GetPageFileSize();
    Task<(int ExitCode, string Output)> RunAsync(string fileName, string args, bool elevate, CancellationToken ct);
    Task<IReadOnlyList<RestorePoint>> GetRestorePointsAsync(CancellationToken ct = default);
}

public sealed class SystemMaintenanceService : ISystemMaintenanceService
{
    public bool IsHibernationEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("HibernateEnabled") is int v && v != 0;
        }
        catch { return false; }
    }

    public long GetHibernationFileSize()
    {
        try
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            // hiberfil.sys liegt auf System-Drive, nicht in Windows-Ordner
            var drive = System.IO.Path.GetPathRoot(win) ?? "C:\\";
            var path = System.IO.Path.Combine(drive, "hiberfil.sys");
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch { }
        return 0;
    }

    public long GetPageFileSize()
    {
        try
        {
            var drive = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var path = System.IO.Path.Combine(drive, "pagefile.sys");
            if (File.Exists(path)) return new FileInfo(path).Length;
            path = System.IO.Path.Combine(drive, "swapfile.sys");
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch { }
        return 0;
    }

    public async Task<(int ExitCode, string Output)> RunAsync(string fileName, string args, bool elevate, CancellationToken ct)
    {
        if (elevate)
        {
            // Bei runas dürfen wir kein RedirectStandardOutput nutzen — UAC schreibt nicht zurück.
            try
            {
                var psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = false,
                };
                using var p = Process.Start(psi);
                if (p is null) return (-1, "Konnte Elevation nicht starten.");
                await p.WaitForExitAsync(ct);
                return (p.ExitCode, $"(Elevation) {fileName} beendet mit Code {p.ExitCode}");
            }
            catch (Exception ex)
            {
                return (-1, "Elevation abgebrochen: " + ex.Message);
            }
        }

        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "Konnte Prozess nicht starten.");

            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = (stdout + Environment.NewLine + stderr).Trim();
            return (p.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public Task<IReadOnlyList<RestorePoint>> GetRestorePointsAsync(CancellationToken ct = default)
    {
        return Task.Run(IReadOnlyList<RestorePoint> () =>
        {
            var list = new List<RestorePoint>();
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\default", "SELECT * FROM SystemRestore");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    if (ct.IsCancellationRequested) break;
                    var seq = Convert.ToInt64(obj["SequenceNumber"]);
                    var desc = obj["Description"]?.ToString() ?? "";
                    var typeNum = Convert.ToInt32(obj["RestorePointType"]);
                    var creation = obj["CreationTime"]?.ToString() ?? "";
                    var dt = ParseWmiDate(creation);
                    list.Add(new RestorePoint
                    {
                        SequenceNumber = seq,
                        Description = desc,
                        Type = TypeName(typeNum),
                        CreationTimeUtc = dt,
                        SizeBytes = 0,
                        Drive = "System",
                    });
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            return list.OrderByDescending(r => r.CreationTimeUtc).ToList();
        }, ct);
    }

    private static DateTime ParseWmiDate(string s)
    {
        // Format: yyyyMMddHHmmss.ffffff+TZ
        if (s.Length < 14) return DateTime.MinValue;
        if (DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt))
            return dt.ToUniversalTime();
        return DateTime.MinValue;
    }

    private static string TypeName(int type) => type switch
    {
        0 => "Anwendung installiert",
        1 => "Anwendung deinstalliert",
        6 => "Manuell",
        7 => "Automatisch",
        10 => "Geänderte Einstellungen",
        12 => "Windows Update",
        13 => "System-Check",
        _ => $"Typ {type}",
    };
}
