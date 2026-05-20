using Cleaner.App.ViewModels.Pages;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class SystemMaintenancePage : INavigableView<SystemMaintenanceViewModel>
{
    public SystemMaintenanceViewModel ViewModel { get; }

    public SystemMaintenancePage(SystemMaintenanceViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        Loaded += (_, _) => ViewModel.RefreshStatus();
    }
}
