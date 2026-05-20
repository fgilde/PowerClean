using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class DuplicatesPage : INavigableView<DuplicatesViewModel>
{
    public DuplicatesViewModel ViewModel { get; }

    public DuplicatesPage(DuplicatesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
