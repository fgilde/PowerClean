using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class DeliveryOptimizationCleaner : CleanupTargetBase
{
    public DeliveryOptimizationCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.delivery-optimization";
    public override string Name => "Delivery-Optimization-Cache";
    public override string Description =>
        "Peer-zu-Peer Windows-Update-Cache. Kann hunderte MB belegen und wird neu aufgebaut.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Recommended;
    public override bool RequiresAdmin => true;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var sysData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        yield return Path.Combine(sysData, "Microsoft", "Network", "Downloader");
    }
}
