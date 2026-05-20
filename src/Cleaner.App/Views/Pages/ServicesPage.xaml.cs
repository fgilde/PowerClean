using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class ServicesPage : INavigableView<ServicesViewModel>
{
    public ServicesViewModel ViewModel { get; }

    public ServicesPage(ServicesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
