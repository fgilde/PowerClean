using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class CleanupHistoryPage : INavigableView<CleanupHistoryViewModel>
{
    public CleanupHistoryViewModel ViewModel { get; }

    public CleanupHistoryPage(CleanupHistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
