using Cleaner.Core.Cleaners;
using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public sealed class CleanerRegistry : ICleanerRegistry
{
    public CleanerRegistry(IEnumerable<ICleanupTarget> cleaners)
    {
        All = cleaners.OrderBy(c => c.Category).ThenBy(c => c.SafetyLevel).ThenBy(c => c.Name).ToList();
    }

    public IReadOnlyList<ICleanupTarget> All { get; }

    public IEnumerable<ICleanupTarget> ByCategory(CleanupCategory category)
        => All.Where(c => c.Category == category);
}
