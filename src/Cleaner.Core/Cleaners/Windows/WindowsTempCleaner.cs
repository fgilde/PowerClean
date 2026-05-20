using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class WindowsTempCleaner : CleanupTargetBase
{
    public WindowsTempCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.windows-temp";
    public override string Name => "Windows Temp-Ordner";
    public override string Description =>
        "C:\\Windows\\Temp — System-temporäre Dateien. Benötigt Admin-Rechte.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Safe;
    public override bool RequiresAdmin => true;

    protected override TimeSpan? MinimumAge => TimeSpan.FromHours(1);

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        yield return Path.Combine(windir, "Temp");
    }
}
