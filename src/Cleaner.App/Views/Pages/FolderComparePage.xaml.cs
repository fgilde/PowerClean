using System.Windows.Input;
using Cleaner.App.ViewModels.Pages;
using Cleaner.Core.Services;
using Wpf.Ui.Controls;

namespace Cleaner.App.Views.Pages;

public partial class FolderComparePage : INavigableView<FolderCompareViewModel>
{
    public FolderCompareViewModel ViewModel { get; }

    public FolderComparePage(FolderCompareViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.DataGrid grid
            && grid.SelectedItem is CompareEntry entry
            && entry.Status == CompareStatus.Different
            && entry.LeftFullPath is not null
            && entry.RightFullPath is not null)
        {
            ViewModel.OpenFileDiffCommand.Execute(entry);
        }
    }
}
