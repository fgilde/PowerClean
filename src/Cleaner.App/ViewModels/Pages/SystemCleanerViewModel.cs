using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels.Pages;

public sealed class SystemCleanerViewModel : CleanerPageViewModelBase
{
    public SystemCleanerViewModel(ICleanerRegistry registry, AppSettings settings, Cleaner.App.Services.RunningTaskRegistry taskRegistry)
        : base(registry, settings, taskRegistry)
    {
        LoadTargets(t => t.Category is CleanupCategory.WindowsSystem or CleanupCategory.Browsers or CleanupCategory.Logs);
    }
}
