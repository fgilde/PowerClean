using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class AutostartPage : INavigableView<AutostartViewModel>
{
    public AutostartViewModel ViewModel { get; }

    public AutostartPage(AutostartViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
