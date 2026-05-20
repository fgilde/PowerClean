using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Utils;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class MemoryDumpCleaner : CleanupTargetBase
{
    public MemoryDumpCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.memory-dumps";
    public override string Name => "Crash- & Memory-Dumps";
    public override string Description =>
        "Minidumps und Memory-Dump-Dateien. Nur löschen, wenn aktuell kein Bluescreen analysiert werden soll.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Warning;
    public override bool RequiresAdmin => true;

    public override Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            long size = 0;
            int count = 0;
            var paths = new List<string>();

            var minidumpDir = Path.Combine(windir, "Minidump");
            if (Directory.Exists(minidumpDir))
            {
                foreach (var f in SafeEnumerator.EnumerateFiles(minidumpDir, "*.dmp", recursive: false))
                {
                    paths.Add(f);
                    size += SafeEnumerator.TryGetSize(f);
                    count++;
                }
            }

            var bigDump = Path.Combine(windir, "MEMORY.DMP");
            if (File.Exists(bigDump))
            {
                paths.Add(bigDump);
                size += SafeEnumerator.TryGetSize(bigDump);
                count++;
            }

            return new ScanResult { TargetId = Id, SizeBytes = size, FileCount = count, Paths = paths };
        }, ct);
    }

    protected override IEnumerable<string> EnumerateCleanupRoots() => Array.Empty<string>();
}
