using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class PrefetchCleaner : CleanupTargetBase
{
    public PrefetchCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.prefetch";
    public override string Name => "Prefetch-Daten";
    public override string Description =>
        "Windows Prefetch-Files. Werden für Schnellstart benutzt — kurzfristig langsamerer Programmstart. " +
        "Älter als 30 Tage werden vorgeschlagen.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Caution;
    public override bool RequiresAdmin => true;

    protected override TimeSpan? MinimumAge => TimeSpan.FromDays(30);
    protected override string FilePattern => "*.pf";

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        yield return Path.Combine(windir, "Prefetch");
    }
}
