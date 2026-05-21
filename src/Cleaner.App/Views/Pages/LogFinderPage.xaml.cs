using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class LogFinderPage : INavigableView<LogFinderViewModel>
{
    public LogFinderViewModel ViewModel { get; }

    public LogFinderPage(LogFinderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
