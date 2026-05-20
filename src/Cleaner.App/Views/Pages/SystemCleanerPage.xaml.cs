using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class SystemCleanerPage : INavigableView<SystemCleanerViewModel>
{
    public SystemCleanerViewModel ViewModel { get; }

    public SystemCleanerPage(SystemCleanerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
