using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class ProcessMonitorPage : INavigableView<ProcessMonitorViewModel>
{
    public ProcessMonitorViewModel ViewModel { get; }

    public ProcessMonitorPage(ProcessMonitorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ViewModel.Activate(); else ViewModel.Deactivate();
        };
    }
}
