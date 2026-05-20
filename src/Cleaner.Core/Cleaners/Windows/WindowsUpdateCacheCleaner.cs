using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class WindowsUpdateCacheCleaner : CleanupTargetBase
{
    public WindowsUpdateCacheCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.windows-update-cache";
    public override string Name => "Windows-Update-Cache";
    public override string Description =>
        "C:\\Windows\\SoftwareDistribution\\Download — bereits installierte Update-Pakete. " +
        "Empfohlen den Windows-Update-Dienst vorher zu stoppen. Admin-Rechte nötig.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;
    public override bool RequiresAdmin => true;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        yield return Path.Combine(windir, "SoftwareDistribution", "Download");
    }
}
