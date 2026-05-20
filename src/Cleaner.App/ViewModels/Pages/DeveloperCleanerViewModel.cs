using Cleaner.Core.Models;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels.Pages;

public sealed class DeveloperCleanerViewModel : CleanerPageViewModelBase
{
    public DeveloperCleanerViewModel(ICleanerRegistry registry, AppSettings settings, Cleaner.App.Services.RunningTaskRegistry taskRegistry)
        : base(registry, settings, taskRegistry)
    {
        LoadTargets(t => t.Category == CleanupCategory.Developer);
    }
}
