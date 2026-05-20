using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class LargeFilesPage : INavigableView<LargeFilesViewModel>
{
    public LargeFilesViewModel ViewModel { get; }

    public LargeFilesPage(LargeFilesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
