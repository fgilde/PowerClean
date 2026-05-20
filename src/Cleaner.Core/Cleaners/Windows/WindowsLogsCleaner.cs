using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class WindowsLogsCleaner : CleanupTargetBase
{
    public WindowsLogsCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.windows-logs";
    public override string Name => "Windows-Logs";
    public override string Description =>
        "Setup- und Servicing-Logs in C:\\Windows\\Logs. Älter als 14 Tage werden gefunden.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.Logs;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;
    public override bool RequiresAdmin => true;

    protected override TimeSpan? MinimumAge => TimeSpan.FromDays(14);

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        yield return Path.Combine(windir, "Logs");
    }
}
