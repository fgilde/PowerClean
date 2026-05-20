using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class InstalledProgramsPage : INavigableView<InstalledProgramsViewModel>
{
    public InstalledProgramsViewModel ViewModel { get; }

    public InstalledProgramsPage(InstalledProgramsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
