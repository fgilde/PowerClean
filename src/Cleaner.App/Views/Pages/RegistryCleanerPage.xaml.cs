using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class RegistryCleanerPage : INavigableView<RegistryCleanerViewModel>
{
    public RegistryCleanerViewModel ViewModel { get; }

    public RegistryCleanerPage(RegistryCleanerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
