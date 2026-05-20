using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public sealed class InstalledProgram
{
    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public DateTime? InstallDate { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public string? InstallLocation { get; init; }
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? IconPath { get; init; }
    public required string RegistryKey { get; init; }
    public required bool IsSystemComponent { get; init; }
    public bool Is64Bit { get; init; }

    public string Scope { get; init; } = "User";
}

public interface IInstalledProgramsScanner
{
    Task<IReadOnlyList<InstalledProgram>> ScanAsync(CancellationToken ct = default);
    bool Uninstall(InstalledProgram program, bool quiet);
}

public sealed class InstalledProgramsScanner : IInstalledProgramsScanner
{
    private static readonly string[] UninstallPaths =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    public Task<IReadOnlyList<InstalledProgram>> ScanAsync(CancellationToken ct = default)
    {
        return Task.Run(IReadOnlyList<InstalledProgram> () =>
        {
            var list = new List<InstalledProgram>();

            foreach (var path in UninstallPaths)
            {
                if (ct.IsCancellationRequested) break;
                list.AddRange(ScanHive(Registry.LocalMachine, path, "All Users", path.Contains("Wow6432") ? false : Environment.Is64BitOperatingSystem));
                list.AddRange(ScanHive(Registry.CurrentUser, path, "Current User", path.Contains("Wow6432") ? false : Environment.Is64BitOperatingSystem));
            }

            // Deduplizieren: gleicher DisplayName + Version = ein Eintrag
            var deduped = list
                .GroupBy(p => (p.DisplayName, p.Version ?? ""))
                .Select(g => g.First())
                .OrderByDescending(p => p.EstimatedSizeBytes)
                .ThenBy(p => p.DisplayName)
                .ToList();

            return deduped;
        }, ct);
    }

    private static IEnumerable<InstalledProgram> ScanHive(RegistryKey root, string path, string scope, bool is64)
    {
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null) yield break;

        foreach (var subName in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(subName, writable: false);
            if (sub is null) continue;

            var displayName = sub.GetValue("DisplayName")?.ToString();
            if (string.IsNullOrWhiteSpace(displayName)) continue;
            // SystemComponent oder ParentKeyName? Sind meist Updates / interne Komponenten
            bool isSystem = sub.GetValue("SystemComponent") is int sc && sc != 0;
            bool isUpdate = !string.IsNullOrEmpty(sub.GetValue("ParentKeyName")?.ToString());
            if (isUpdate) continue; // Updates ausblenden, sonst zu viel Rauschen

            yield return new InstalledProgram
            {
                DisplayName = displayName,
                Publisher = sub.GetValue("Publisher")?.ToString(),
                Version = sub.GetValue("DisplayVersion")?.ToString() ?? sub.GetValue("Version")?.ToString(),
                InstallDate = ParseInstallDate(sub.GetValue("InstallDate")?.ToString()),
                EstimatedSizeBytes = ParseSize(sub),
                InstallLocation = sub.GetValue("InstallLocation")?.ToString(),
                UninstallString = sub.GetValue("UninstallString")?.ToString(),
                QuietUninstallString = sub.GetValue("QuietUninstallString")?.ToString(),
                IconPath = sub.GetValue("DisplayIcon")?.ToString(),
                RegistryKey = $@"{root.Name}\{path}\{subName}",
                IsSystemComponent = isSystem,
                Is64Bit = is64,
                Scope = scope,
            };
        }
    }

    private static long ParseSize(RegistryKey k)
    {
        // EstimatedSize ist in KB
        if (k.GetValue("EstimatedSize") is int kb && kb > 0) return (long)kb * 1024;
        return 0;
    }

    private static DateTime? ParseInstallDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)) return d;
        return null;
    }

    public bool Uninstall(InstalledProgram program, bool quiet)
    {
        var cmd = quiet && !string.IsNullOrWhiteSpace(program.QuietUninstallString)
            ? program.QuietUninstallString
            : program.UninstallString;

        if (string.IsNullOrWhiteSpace(cmd)) return false;

        try
        {
            string file, args;
            if (cmd.StartsWith("\""))
            {
                int closeIdx = cmd.IndexOf('"', 1);
                if (closeIdx < 0) { file = cmd.Trim('"'); args = ""; }
                else { file = cmd[1..closeIdx]; args = cmd[(closeIdx + 1)..].Trim(); }
            }
            else
            {
                int spaceIdx = cmd.IndexOf(' ');
                if (spaceIdx < 0) { file = cmd; args = ""; }
                else { file = cmd[..spaceIdx]; args = cmd[(spaceIdx + 1)..]; }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
            });
            return true;
        }
        catch { return false; }
    }
}
