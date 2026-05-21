using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class OrphanUserDataPage : INavigableView<OrphanUserDataViewModel>
{
    public OrphanUserDataViewModel ViewModel { get; }

    public OrphanUserDataPage(OrphanUserDataViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
