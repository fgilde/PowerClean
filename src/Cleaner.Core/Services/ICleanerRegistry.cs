using Cleaner.Core.Cleaners;
using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public interface ICleanerRegistry
{
    IReadOnlyList<ICleanupTarget> All { get; }
    IEnumerable<ICleanupTarget> ByCategory(CleanupCategory category);
}
