using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.Core.Cleaners.Windows;

public sealed class UserTempCleaner : CleanupTargetBase
{
    public UserTempCleaner(IFileSystemOperations fs) : base(fs) { }

    public override string Id => "system.user-temp";
    public override string Name => "Benutzer-Temp-Dateien";
    public override string Description =>
        "Temporäre Dateien aus %TEMP%. Wird ständig neu befüllt, Löschen ist sicher.";
    public override string IconGlyph => "";
    public override CleanupCategory Category => CleanupCategory.WindowsSystem;
    public override SafetyLevel SafetyLevel => SafetyLevel.Safe;

    // Sehr junge Dateien überspringen (Programme könnten sie gerade benutzen)
    protected override TimeSpan? MinimumAge => TimeSpan.FromHours(1);

    protected override IEnumerable<string> EnumerateCleanupRoots()
    {
        yield return Path.GetTempPath();
    }
}
