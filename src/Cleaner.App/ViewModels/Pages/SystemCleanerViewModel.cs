using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels.Pages;

public sealed class SystemCleanerViewModel : CleanerPageViewModelBase
{
    public SystemCleanerViewModel(ICleanerRegistry registry, AppSettings settings,
        Cleaner.App.Services.RunningTaskRegistry taskRegistry, Cleaner.App.Services.CleanupHistoryService history,
        Cleaner.App.Services.ProfileService profiles)
        : base(registry, settings, taskRegistry, history, profiles)
    {
        LoadTargets(t => t.Category is CleanupCategory.WindowsSystem or CleanupCategory.Browsers or CleanupCategory.Logs or CleanupCategory.Other);
    }
}
