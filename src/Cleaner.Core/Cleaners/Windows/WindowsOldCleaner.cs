using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

/// <summary>
/// C:\Windows.old — Reste eines Windows-Upgrades. Erlaubt die Rückkehr zur alten Version
/// (i. d. R. nur 10 Tage), danach nur Ballast. Achtung: Teile sind durch TrustedInstaller
/// geschützt; was nicht löschbar ist, sollte über die Datenträgerbereinigung entfernt werden.
/// </summary>
public sealed class WindowsOldCleaner : CleanupTargetBase
{
    public WindowsOldCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.windows-old";
    public override string Name => "Windows.old (alte Installation)";
    public override string Description =>
        "C:\\Windows.old — Reste eines Windows-Upgrades. Nach dem Löschen ist kein Downgrade " +
        "zur Vorversion mehr möglich. Admin-Rechte nötig.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Warning;
    public override bool RequiresAdmin => true;

    protected override bool PreserveRootDirectories => false;

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        yield return Path.Combine(systemDrive + "\\", "Windows.old");
    }
}
