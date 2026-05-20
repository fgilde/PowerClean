using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class DeveloperCleanerPage : INavigableView<DeveloperCleanerViewModel>
{
    public DeveloperCleanerViewModel ViewModel { get; }

    public DeveloperCleanerPage(DeveloperCleanerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
